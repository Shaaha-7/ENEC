"""Run every Qandeel simulation and write figures plus a results summary.

    python -m qandeel.run_all            # writes qandeel/outputs/

Uses the real Gulf record in qandeel/data/gulf_forcing.csv when it exists
(create it with `python -m qandeel.data_fetch`), otherwise assumed conditions.
Outputs demonstrate the concept; they are not site predictions.
"""
import json
from pathlib import Path

import matplotlib

matplotlib.use("Agg")
import matplotlib.pyplot as plt  # noqa: E402
import numpy as np  # noqa: E402

from qandeel.sim.boom import boom_grid, run_boom  # noqa: E402
from qandeel.sim.curtain import CURTAIN_Y, GAP_W, bulson_surface_current, hold_grid, run_hold  # noqa: E402
from qandeel.sim.env import WIND_DRIFT_FACTOR, Conditions, bearing_to_xy  # noqa: E402
from qandeel.sim.forcing import load_record, load_site, source_label, with_wind_factor  # noqa: E402
from qandeel.sim.planning import forecast_arrival, plan_release, switch_on_time  # noqa: E402
from qandeel.sim.benefits import adaptive_airflow, cost_summary, emissions, hold_margin, smart_switching  # noqa: E402
from qandeel.sim.sensitivity import tornado  # noqa: E402
from qandeel.sim.sizing import boom_throughput, compressor, herding_logistics, jellyfish_mass_kg  # noqa: E402

OUT = Path(__file__).parent / "outputs"

# Palette: one quiet grey for context, one blue accent, ordered blues for levels.
INK, INK2, MUTED, GRID = "#0b0b0b", "#52514e", "#898781", "#e4e3df"
GREY, ACCENT = "#b9b8b2", "#2a78d6"
BLUES = {0.0: GREY, 1.5: "#86b6ef", 3.0: "#2a78d6", 4.5: "#104281"}
SKIRT_BLUES = {1.0: "#86b6ef", 1.5: "#2a78d6", 2.0: "#104281"}
GOOD_GREEN = "#0ca30c"
SURFACE = "#fcfcfb"

ALERTS = [  # (label, distance km, bearing deg)
    ("Swarm 15 km NNW", 15, 330),
    ("Swarm 28 km WNW", 28, 285),
    ("Swarm 28 km N", 28, 0),
]
RELATIVE_TOW = 0.20  # m/s through the water with the retention bag, from the boom simulation
DESIGN_SKIRT = 2.0


def _style(ax, title, subtitle=None):
    ax.set_facecolor(SURFACE)
    for s in ("top", "right"):
        ax.spines[s].set_visible(False)
    for s in ("left", "bottom"):
        ax.spines[s].set_color(MUTED)
    ax.tick_params(colors=INK2, labelsize=9)
    ax.grid(color=GRID, linewidth=0.8)
    ax.set_axisbelow(True)
    ax.set_title(title, loc="left", fontsize=12, fontweight="bold", color=INK, pad=22 if subtitle else 8)
    if subtitle:
        ax.text(0, 1.02, subtitle, transform=ax.transAxes, fontsize=9, color=INK2)


def fig_arrival(fc, t_on, label, src):
    fig, (ax1, ax2) = plt.subplots(1, 2, figsize=(11.5, 4.8), facecolor=SURFACE,
                                   gridspec_kw={"width_ratios": [1, 1.15]})
    c = fc.cloud
    for r in range(min(25, c.centroid.shape[1])):
        ax1.plot(c.centroid[:, r, 0] / 1000, c.centroid[:, r, 1] / 1000, color=GREY, lw=0.8, alpha=0.7)
    p0 = c.hourly[0, 0] / 1000
    ax1.scatter(p0[:, 0], p0[:, 1], s=8, color=INK, lw=0, label="swarm at alert (one run)")
    h = int(min(len(c.hourly) - 1, round(fc.p50_h or 24)))
    ph = c.hourly[h, 0] / 1000
    ax1.scatter(ph[:, 0], ph[:, 1], s=8, color=ACCENT, lw=0, label=f"same swarm after {h} h")
    ax1.axhline(0, color=INK2, lw=1.5)
    ax1.plot([0], [0], marker="v", color=INK, ms=9)
    ax1.annotate("intake gap", (0, 0), xytext=(8, -14), textcoords="offset points", fontsize=9, color=INK)
    ax1.set_xlim(-25, 25)
    ax1.set_ylim(-1.5, 18)
    ax1.set_xlabel("km east of gap", color=INK2, fontsize=9)
    ax1.set_ylabel("km offshore", color=INK2, fontsize=9)
    ax1.legend(frameon=False, fontsize=8.5, loc="upper right", labelcolor=INK2)
    _style(ax1, f"{label}: 60 jellyfish per swarm, 400 runs", "grey = swarm centre paths")

    lead = fc.lead_h[~np.isnan(fc.lead_h)]
    ax2.hist(lead, bins=np.arange(0, 97, 3), color=GREY, edgecolor=SURFACE, linewidth=1)
    ax2.axvspan(fc.p10_h, fc.p90_h, color=ACCENT, alpha=0.10)
    ax2.axvline(fc.p50_h, color=ACCENT, lw=2)
    ax2.axvline(t_on, color=INK, lw=1.5, ls="--")
    top = ax2.get_ylim()[1]
    ax2.text(t_on, top * 0.95, f"curtain on: {t_on:.0f} h ", fontsize=9, color=INK, ha="right")
    ax2.text(fc.p50_h, top * 0.85, f" bulk arrives: {fc.p50_h:.0f} h", fontsize=9, color=ACCENT)
    ax2.text(fc.p90_h, top * 0.70, f" window {fc.p10_h:.0f}-{fc.p90_h:.0f} h", fontsize=9, color=INK2)
    ax2.set_xlabel("hours after alert until the first 5% of the swarm reaches the gap", color=INK2, fontsize=9)
    ax2.set_ylabel("simulated runs", color=INK2, fontsize=9)
    _style(ax2, f"{fc.p_arrive:.0%} of runs reach the gap; curtain switches on at {t_on:.0f} h",
           f"Forcing: {src}")
    fig.tight_layout(w_pad=4)
    fig.savefig(OUT / "fig1_arrival_window.png", dpi=160)
    plt.close(fig)


def fig_hold(rows):
    fig, axes = plt.subplots(1, 2, figsize=(12, 4.6), facecolor=SURFACE, sharey=True)
    for ax, sw in zip(axes, (0.10, 0.20)):
        for q in (0.0, 1.5, 3.0, 4.5):
            rr = [r for r in rows if r["airflow_l_s_m"] == q and r["swim_max_m_s"] == sw]
            name = "curtain off" if q == 0 else f"{q:g} L/s per m ({bulson_surface_current(q):.2f} m/s current)"
            ax.plot([r["approach_m_s"] for r in rr], [100 * r["held_share"] for r in rr], color=BLUES[q],
                    lw=2, marker="o", ms=5, label=name)
        ax.axvspan(0.0, 0.10, color=GRID, alpha=0.6)
        ax.set_xlim(0.03, 0.42)
        ax.set_ylim(-3, 105)
        ax.set_xlabel("approach current toward the gap (m/s)", color=INK2, fontsize=9)
        _style(ax, f"Jellyfish swimming up to {sw * 100:.0f} cm/s",
               "field data (>14 cm)" if sw == 0.10 else "faster adults, never measured")
    axes[0].set_ylabel("held outside the gap after 3 h (%)", color=INK2, fontsize=9)
    axes[0].text(0.052, 4, "typical sheltered\ngap flow (assumed)", fontsize=8, color=INK2)
    axes[1].legend(frameon=False, fontsize=8.5, loc="center left", bbox_to_anchor=(1.01, 0.5),
                   labelcolor=INK2, title="bubble airflow", title_fontsize=9)
    fig.suptitle("At 3 L/s per m the curtain holds the swarm up to 0.2 m/s, even if adults swim 20 cm/s",
                 x=0.01, ha="left", fontsize=12, fontweight="bold", color=INK)
    fig.tight_layout()
    fig.savefig(OUT / "fig2_curtain_hold.png", dpi=160)
    plt.close(fig)


def fig_paths():
    fig, axes = plt.subplots(1, 2, figsize=(11, 4.4), facecolor=SURFACE, sharey=True)
    half = GAP_W / 2
    for ax, q, title in ((axes[0], 0.0, "Curtain off: {:.0%} enter the gap in 3 h"),
                         (axes[1], 3.0, "Curtain on (3 L/s per m): {:.0%} enter")):
        res = run_hold(0.15, q, n=400, seed=3)
        edge = half + 200
        ax.plot([-edge, -half], [0, 0], color=INK2, lw=4, solid_capstyle="butt")
        ax.plot([half, edge], [0, 0], color=INK2, lw=4, solid_capstyle="butt")
        if q:
            ax.plot([-half, half], [CURTAIN_Y, CURTAIN_Y], color=ACCENT, lw=2, ls=(0, (2, 2)))
            ax.text(-half + 4, CURTAIN_Y + 14, "bubble curtain", ha="left", fontsize=9, color=ACCENT)
        ax.scatter(res.start_xy[:, 0], res.start_xy[:, 1], s=6, color=GREY, alpha=0.5, lw=0)
        held = ~res.entered_mask
        ax.scatter(res.final_xy[held, 0], res.final_xy[held, 1], s=9, color=INK, alpha=0.6, lw=0)
        ax.text(0, -40, "intake gap", ha="center", fontsize=9, color=INK2)
        ax.set_xlim(-half - 150, half + 150)
        ax.set_ylim(-70, 520)
        ax.set_xlabel("metres along the breakwater", color=INK2, fontsize=9)
        _style(ax, title.format(1 - res.held_share))
    axes[0].set_ylabel("metres offshore", color=INK2, fontsize=9)
    axes[0].text(0.98, 0.95, "grey = starting positions", transform=axes[0].transAxes, ha="right",
                 va="top", fontsize=8.5, color=INK2)
    axes[1].text(0.98, 0.95, "grey = starting positions\nblack = still outside after 3 h,\nbunched for the boom",
                 transform=axes[1].transAxes, ha="right", va="top", fontsize=8.5, color=INK2)
    fig.tight_layout()
    fig.savefig(OUT / "fig3_curtain_positions.png", dpi=160)
    plt.close(fig)


def fig_release(options, src):
    fig, ax = plt.subplots(figsize=(10, 5.6), facecolor=SURFACE)
    ax.axhline(0, color=INK2, lw=1.5)
    ax.plot([0], [0], marker="v", color=INK, ms=9)
    ax.annotate("intake gap", (0, 0), xytext=(6, -14), textcoords="offset points", fontsize=9, color=INK)
    best_per_d = {}
    for o in options:
        best_per_d.setdefault(o.distance_km, o)
        x, y = bearing_to_xy(o.distance_km, o.bearing_deg) / 1000
        ax.scatter([x], [y], s=22, color=GRID, zorder=2)
    for d, o in best_per_d.items():
        x, y = bearing_to_xy(o.distance_km, o.bearing_deg) / 1000
        chosen = o is options[0]
        ax.scatter([x], [y], s=70, color=ACCENT if chosen else GREY, zorder=3)
        ax.annotate(f"{d:g} km: {o.p_return:.0%} back, {o.tow_hours:.0f} h tow", (x, y), xytext=(9, -15 if chosen else 4),
                    textcoords="offset points", fontsize=8.5, color=ACCENT if chosen else INK2,
                    fontweight="bold" if chosen else "normal",
                    bbox={"facecolor": SURFACE, "edgecolor": "none", "pad": 0.5, "alpha": 0.85})
    ax.set_xlim(-17, 17)
    ax.set_ylim(-1.5, 17)
    ax.set_aspect("equal")
    ax.set_xlabel("km east of gap", color=INK2, fontsize=9)
    ax.set_ylabel("km offshore", color=INK2, fontsize=9)
    best = options[0]
    _style(ax, f"Best release for net removal: {best.distance_km:g} km, {best.tow_hours:.0f} h tow, "
               f"{best.p_return:.0%} drift back to the curtain",
           "Labelled: best point at each distance (share back at the gap within 72 h). Far = fewer returns, longer tows.")
    fig.tight_layout()
    fig.savefig(OUT / "fig4_release_points.png", dpi=160)
    plt.close(fig)


def fig_boom(rows):
    fig, axes = plt.subplots(1, 2, figsize=(12, 4.6), facecolor=SURFACE, sharey=True)
    for ax, sw in zip(axes, (0.10, 0.20)):
        for d in (1.0, 1.5, 2.0):
            rr = [r for r in rows if r["swim_max_m_s"] == sw and r["skirt_m"] == d
                  and r["downflow_fraction"] == 0.25 and not r["bag"]]
            ax.plot([r["tow_m_s"] for r in rr], [100 * r["retained_share"] for r in rr], color=SKIRT_BLUES[d],
                    lw=2, marker="o", ms=5, label=f"{d:g} m skirt, open")
        for k, ls in ((0.25, "-"), (0.5, ":")):
            rr = [r for r in rows if r["swim_max_m_s"] == sw and r["bag"] and r["downflow_fraction"] == k]
            ax.plot([r["tow_m_s"] for r in rr], [100 * r["retained_share"] for r in rr], color=GOOD_GREEN,
                    lw=2.5, ls=ls, marker="s", ms=5,
                    label="2 m skirt + retention bag" + (" (pessimistic downflow)" if k == 0.5 else ""))
        ax.axvline(0.35, color=MUTED, lw=1, ls="--")
        ax.text(0.355, 92, "oil-boom\nlimit", fontsize=8, color=INK2)
        ax.set_ylim(-3, 105)
        ax.set_xlabel("boom speed through the water (m/s)", color=INK2, fontsize=9)
        _style(ax, f"Jellyfish swimming up to {sw * 100:.0f} cm/s", "30 min in the boom pocket")
    axes[0].set_ylabel("jellyfish kept in the boom (%)", color=INK2, fontsize=9)
    axes[1].legend(frameon=False, fontsize=8.5, loc="center left", bbox_to_anchor=(1.01, 0.5), labelcolor=INK2)
    fig.suptitle("Open booms leak jellyfish; a closed-bottom retention bag keeps over 90% up to 0.3 m/s (model)",
                 x=0.01, ha="left", fontsize=12, fontweight="bold", color=INK)
    fig.tight_layout()
    fig.savefig(OUT / "fig5_boom_retention.png", dpi=160)
    plt.close(fig)


def fig_tornado(rows):
    outcomes = list(dict.fromkeys(r["outcome"] for r in rows))
    fig, axes = plt.subplots(len(outcomes), 1, figsize=(10, 2.0 + 1.25 * len(rows) / len(outcomes) * len(outcomes) / 1.6),
                             facecolor=SURFACE)
    for ax, oc in zip(axes, outcomes):
        rr = [r for r in rows if r["outcome"] == oc]
        rr.sort(key=lambda r: abs(r["high_value"] - r["low_value"]))
        base = float(rr[0]["base"])
        for i, r in enumerate(rr):
            lo, hi = float(r["low_value"]), float(r["high_value"])
            ax.plot([min(lo, hi), max(lo, hi)], [i, i], color=ACCENT if r is rr[-1] else GREY, lw=9,
                    solid_capstyle="butt")
            if abs(hi - lo) < 0.02 * max(abs(base), 1):
                ax.text(base, i, "  little effect", va="center", fontsize=7.5, color=INK2)
            else:
                ax.text(lo, i + 0.32, f"{r['low']:g}", ha="center", fontsize=7.5, color=INK2)
                ax.text(hi, i + 0.32, f"{r['high']:g}", ha="center", fontsize=7.5, color=INK2)
        ax.axvline(base, color=INK, lw=1, ls="--")
        ax.set_yticks(range(len(rr)))
        ax.set_yticklabels([r["unknown"] for r in rr], fontsize=9, color=INK)
        ax.set_ylim(-0.6, len(rr) - 0.2)
        _style(ax, oc, f"base case {base:g}; bar = result at low and high value of each unknown")
    fig.suptitle("What to measure first: approach current, apex downflow and gap width drive the results",
                 x=0.01, ha="left", fontsize=12, fontweight="bold", color=INK)
    fig.tight_layout(rect=(0, 0, 1, 0.97))
    fig.savefig(OUT / "fig6_sensitivity.png", dpi=160)
    plt.close(fig)


def fig_benefits(sw, approach, q_set, ad, cap):
    fig, (ax1, ax2, ax3) = plt.subplots(1, 3, figsize=(16, 4.7), facecolor=SURFACE,
                                        gridspec_kw={"width_ratios": [1, 1, 1.5]})
    names = ["Every alert\n(96 h each)", "Camera only\n(daylight)", "Qandeel\n+ camera backup"]
    hours = [sw["hours_every_alert"], sw["hours_camera_only"], sw["hours_qandeel"]]
    cols = [GREY, GREY, ACCENT]
    ax1.bar(names, hours, color=cols, width=0.6)
    for i, v in enumerate(hours):
        ax1.text(i, v + max(hours) * 0.02, f"{v:,.0f} h", ha="center", fontsize=10, color=INK)
    ax1.set_ylim(0, max(hours) * 1.18)
    ax1.tick_params(axis="x", labelsize=8.5)
    ax1.set_ylabel(f"curtain running hours, {sw['alerts']} alerts", color=INK2, fontsize=9)
    _style(ax1, "Curtain hours", "camera only runs least")
    unp = [100.0, sw["unprotected_pct_camera_only"], sw["unprotected_pct_qandeel"]]
    ax2.bar(names[1:], unp[1:], color=cols[1:], width=0.5)
    for i, v in enumerate(unp[1:]):
        ax2.text(i, v + 0.6, f"{v:.1f}%", ha="center", fontsize=10, color=INK)
    ax2.set_ylim(0, max(unp[1:]) * 1.3 + 1)
    ax2.tick_params(axis="x", labelsize=8.5)
    ax2.set_ylabel("arriving jellyfish reaching the gap\nbefore the curtain runs (%)", color=INK2, fontsize=9)
    _style(ax2, "Jellyfish that get through first", "night arrivals are missed by a camera")
    hrs = np.arange(len(q_set)) / 24
    ax3.plot(hrs, q_set, color=ACCENT, lw=0.8)
    ax3.axhline(4.5, color=INK2, lw=1, ls="--")
    ax3.text(hrs[-1], 4.62, f"design maximum 4.5 (holds ~{cap[4.5]:.2f} m/s)", ha="right", fontsize=8.5, color=INK2)
    ax3.axhline(3.0, color=INK2, lw=0.8, ls=":")
    ax3.text(hrs[-1], 3.1, f"normal 3.0 (holds ~{cap[3.0]:.2f} m/s)", ha="right", fontsize=8.5, color=INK2,
             bbox={"facecolor": SURFACE, "edgecolor": "none", "pad": 1})
    ax3.axhline(q_set.mean(), color=GOOD_GREEN, lw=1.5)
    ax3.text(1, 0.35, f"green = adaptive average {q_set.mean():.1f} L/s per m", fontsize=8.5, color=GOOD_GREEN,
             bbox={"facecolor": SURFACE, "edgecolor": "none", "pad": 1})
    ax3.set_ylim(0, 5.4)
    ax3.set_xlabel("days from 1 June 2025", color=INK2, fontsize=9)
    ax3.set_ylabel("airflow needed (L/s per m)", color=INK2, fontsize=9)
    _style(ax3, f"Approach at the gap: mean {ad['approach_mean_m_s']:.2f} m/s, worst 5% {ad['approach_p95_m_s']:.2f} m/s",
           f"intake draw + current + wind drift; {100 * ad['share_hours_above_max']:.0f}% of hours exceed the design maximum")
    fig.suptitle(f"Model-error stress test, {sw['alerts']} alerts in the real 2025 season: Qandeel lets "
                 f"{sw['unprotected_pct_qandeel']:.1f}% through vs {sw['unprotected_pct_camera_only']:.0f}% for a camera alone",
                 x=0.01, ha="left", fontsize=12, fontweight="bold", color=INK)
    fig.tight_layout(w_pad=3)
    fig.savefig(OUT / "fig7_smart_operation.png", dpi=160)
    plt.close(fig)


def main():
    OUT.mkdir(exist_ok=True)
    cond, record, site = Conditions(), load_record(), load_site()
    src = source_label(record)
    results = {"forcing": src, "site": site, "alerts": []}

    for i, (label, dist, bearing) in enumerate(ALERTS):
        fc = forecast_arrival(dist, bearing, cond=cond, record=record, seed=i)
        t_on = switch_on_time(fc)
        results["alerts"].append({
            "label": label, "distance_km": dist, "bearing_deg": bearing,
            "p_arrive": round(fc.p_arrive, 3), "share_of_swarm_reaching_gap": round(fc.share_reaching, 3),
            "window_start_h": fc.p10_h and round(fc.p10_h, 1), "bulk_arrival_h": fc.p50_h and round(fc.p50_h, 1),
            "window_end_h": fc.p90_h and round(fc.p90_h, 1), "switch_on_h": t_on and round(t_on, 1),
            "approach_side": fc.side if t_on is not None else None,
        })
        if i == 0:
            fig_arrival(fc, t_on, label, src)
            reached = ~np.isnan(fc.cloud.arrive_h)
            results["natural_fate"] = {
                "label": label, "reach_gap": round(float(reached.mean()), 3),
                "strand_elsewhere": round(float((~np.isnan(fc.cloud.beach_x) & ~reached).mean()), 3)}
            results["arrival_radius_sensitivity"] = [
                {"radius_m": r, "p_arrive": round(forecast_arrival(dist, bearing, cond=cond, record=record, seed=i,
                                                                  arrive_radius_m=r).p_arrive, 3)}
                for r in (500, 1000, 2000)]

    rows = hold_grid()
    results["curtain_hold"] = rows
    fig_hold(rows)
    fig_paths()

    options = plan_release(cond=cond, record=record)  # bearings limited by site.json
    results["release_options"] = [o.__dict__ | {"score": round(o.score, 3), "load_factor": round(o.load_factor, 2)} for o in options]
    fig_release(options, src)

    brows = boom_grid()
    results["boom_retention"] = brows
    fig_boom(brows)

    trows = [r | {"low_value": float(r["low_value"]), "high_value": float(r["high_value"]), "base": float(r["base"])}
             for r in tornado()]
    results["sensitivity"] = trows
    fig_tornado(trows)

    a = results["alerts"][0]
    event_h = (a["window_end_h"] - a["switch_on_h"]) + 12
    results["compressor"] = compressor(curtain_m=GAP_W, airflow_l_s_m=3.0, depth_m=float(site["gap_depth_m"]),
                                       hours=round(event_h))
    retained = run_boom(RELATIVE_TOW, DESIGN_SKIRT, 0.10, 0.25, minutes=30, n=600, bag=True).retained_share
    results["boom"] = boom_throughput(tow_m_s=RELATIVE_TOW, retention=round(retained, 2))  # gathering rate only
    best = {}
    for o in plan_release(cond=cond, record=record, distances_km=(3, 5, 8, 10, 15), min_coast_km=2.0):
        best.setdefault(o.distance_km, o)  # options come sorted best-first
    results["herding"] = herding_logistics([best[d] for d in sorted(best)])
    results["mass_kg"] = {f"{d} cm": round(jellyfish_mass_kg(d), 2) for d in (30, 45)}

    margin, pts = hold_margin()
    cap = {q: v for q, (_, v) in zip((1.5, 3.0, 4.5), pts)}
    results["curtain_capacity_m_s"] = {f"{q:g} L/s per m": round(v, 3) for q, v in cap.items()}
    results["hold_duration"] = [
        {"approach_m_s": v, "swim_max_m_s": sw_, "airflow_l_s_m": 3.0,
         **{f"held_after_{h}h": round(run_hold(v, 3.0, hours=h, n=300, seed=0, swim_max=sw_).held_share, 3)
            for h in (3, 24, 82)}}
        for sw_ in (0.10, 0.20) for v in (0.15, 0.20, 0.25)]
    if record is not None:
        sw, _ = smart_switching(record, n_alerts=100, stress=True)
        sc, _ = smart_switching(record, n_alerts=100, stress=False)
        ad, approach, q_set = adaptive_airflow(record, margin)
        results["smart_switching"], results["smart_switching_self_consistency"] = sw, sc
        results["smart_switching_other_seeds"] = [smart_switching(record, n_alerts=100, seed=s_)[0] for s_ in (8, 9)]
        results["adaptive_airflow"] = ad
        results["adaptive_airflow_wind_3pct"] = adaptive_airflow(with_wind_factor(record, 0.03), margin)[0]
        fig_benefits(sw, approach, q_set, ad, cap)
        sens = []
        for f in (0.0, WIND_DRIFT_FACTOR, 0.03):
            rr = with_wind_factor(record, f)
            al = [forecast_arrival(d_, b_, record=rr, seed=k).p_arrive for k, (_, d_, b_) in enumerate(ALERTS)]
            a_ = adaptive_airflow(rr, margin)[0]
            br = plan_release(record=rr)[0]
            st = smart_switching(rr, n_alerts=100)[0]
            sens.append({"wind_drift_pct": round(100 * f, 1), "p_arrive_alerts": [round(x, 2) for x in al],
                         "approach_mean_m_s": a_["approach_mean_m_s"], "approach_p95_m_s": a_["approach_p95_m_s"],
                         "share_hours_above_design_max": a_["share_hours_above_max"],
                         "best_release": f"{br.distance_km:g} km at {br.bearing_deg:.0f} deg, {br.tow_hours:.0f} h tow, "
                                         f"{br.p_return:.0%} return",
                         "unprotected_pct_camera_only": st["unprotected_pct_camera_only"],
                         "unprotected_pct_qandeel": st["unprotected_pct_qandeel"],
                         "swarms_that_arrived": st["swarms_that_arrived"]})
        results["wind_drift_sensitivity"] = sens
    e = results["compressor"]["energy_mwh_per_event"]
    results["cost"] = cost_summary(e, curtain_m=GAP_W, power_kw=results["compressor"]["power_kw"])
    results["emissions"] = emissions(e)

    (OUT / "results.json").write_text(json.dumps(results, indent=2, default=float))
    print(f"Forcing: {src}")
    print(json.dumps({k: results[k] for k in ("alerts", "compressor", "boom", "cost", "emissions")
                      if k in results} | {k: results[k] for k in ("smart_switching", "smart_switching_self_consistency",
                                                             "adaptive_airflow", "herding", "natural_fate",
                                                             "wind_drift_sensitivity", "hold_duration")
                                          if k in results}, indent=2, default=float))
    print("best release:", results["release_options"][0])


if __name__ == "__main__":
    main()
