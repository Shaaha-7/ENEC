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
from qandeel.sim.env import Conditions, bearing_to_xy  # noqa: E402
from qandeel.sim.forcing import load_record, load_site, source_label  # noqa: E402
from qandeel.sim.planning import forecast_arrival, plan_release, switch_on_time  # noqa: E402
from qandeel.sim.sizing import boom_throughput, compressor, jellyfish_mass_kg  # noqa: E402

OUT = Path(__file__).parent / "outputs"

# Palette: one quiet grey for context, one blue accent, ordered blues for levels.
INK, INK2, MUTED, GRID = "#0b0b0b", "#52514e", "#898781", "#e4e3df"
GREY, ACCENT = "#b9b8b2", "#2a78d6"
BLUES = {0.0: GREY, 1.5: "#86b6ef", 3.0: "#2a78d6", 4.5: "#104281"}
SKIRT_BLUES = {1.0: "#86b6ef", 1.5: "#2a78d6", 2.0: "#104281"}
SURFACE = "#fcfcfb"

ALERTS = [  # (label, distance km, bearing deg)
    ("Swarm 15 km NNW", 15, 330),
    ("Swarm 28 km WNW", 28, 285),
    ("Swarm 28 km N", 28, 0),
]
RELATIVE_TOW = 0.10  # m/s through the water, chosen from the boom simulation
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
        ax.plot([-600, -half], [0, 0], color=INK2, lw=4, solid_capstyle="butt")
        ax.plot([half, 600], [0, 0], color=INK2, lw=4, solid_capstyle="butt")
        if q:
            ax.plot([-half, half], [CURTAIN_Y, CURTAIN_Y], color=ACCENT, lw=2, ls=(0, (2, 2)))
            ax.text(-half + 4, CURTAIN_Y + 14, "bubble curtain", ha="left", fontsize=9, color=ACCENT)
        ax.scatter(res.start_xy[:, 0], res.start_xy[:, 1], s=6, color=GREY, alpha=0.5, lw=0)
        held = ~res.entered_mask
        ax.scatter(res.final_xy[held, 0], res.final_xy[held, 1], s=9, color=INK, alpha=0.6, lw=0)
        ax.text(0, -40, "intake gap", ha="center", fontsize=9, color=INK2)
        ax.set_xlim(-450, 450)
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
    fig, ax = plt.subplots(figsize=(8.5, 4.8), facecolor=SURFACE)
    ax.axhline(0, color=INK2, lw=1.5)
    ax.plot([0], [0], marker="v", color=INK, ms=9)
    ax.annotate("intake gap", (0, 0), xytext=(6, -14), textcoords="offset points", fontsize=9, color=INK)
    for o in options:
        x, y = bearing_to_xy(o.distance_km, o.bearing_deg) / 1000
        ax.scatter([x], [y], s=60, color=ACCENT if o is options[0] else GREY, zorder=3)
        ax.annotate(f"{o.p_return:.0%}", (x, y), xytext=(0, 7), textcoords="offset points",
                    ha="center", fontsize=7.5, color=INK2)
    best = options[0]
    bx, by = bearing_to_xy(best.distance_km, best.bearing_deg) / 1000
    ax.annotate(f"chosen: {best.distance_km:g} km, {best.bearing_deg:.0f} deg", (bx, by), xytext=(-215, -42),
                textcoords="offset points", fontsize=9, color=ACCENT, arrowprops={"arrowstyle": "-", "color": ACCENT})
    ax.set_aspect("equal", adjustable="datalim")
    ax.set_xlabel("km east of gap", color=INK2, fontsize=9)
    ax.set_ylabel("km offshore", color=INK2, fontsize=9)
    _style(ax, f"Best release within a day's tow: {best.distance_km:g} km, {best.p_return:.0%} drift back within 72 h",
           f"Label = share drifting back to the gap; max 24 h tow. Forcing: {src}")
    fig.tight_layout()
    fig.savefig(OUT / "fig4_release_points.png", dpi=160)
    plt.close(fig)


def fig_boom(rows):
    fig, axes = plt.subplots(1, 2, figsize=(12, 4.6), facecolor=SURFACE, sharey=True)
    for ax, sw in zip(axes, (0.10, 0.20)):
        for d in (1.0, 1.5, 2.0):
            rr = [r for r in rows if r["swim_max_m_s"] == sw and r["skirt_m"] == d and r["downflow_fraction"] == 0.25]
            ax.plot([r["tow_m_s"] for r in rr], [100 * r["retained_share"] for r in rr], color=SKIRT_BLUES[d],
                    lw=2, marker="o", ms=5, label=f"{d:g} m skirt")
        ax.axvline(0.35, color=MUTED, lw=1, ls="--")
        ax.text(0.355, 92, "oil-boom\nlimit", fontsize=8, color=INK2)
        ax.set_ylim(-3, 105)
        ax.set_xlabel("boom speed through the water (m/s)", color=INK2, fontsize=9)
        _style(ax, f"Jellyfish swimming up to {sw * 100:.0f} cm/s", "30 min in the boom pocket")
    axes[0].set_ylabel("jellyfish kept in the boom (%)", color=INK2, fontsize=9)
    axes[1].legend(frameon=False, fontsize=8.5, loc="center left", bbox_to_anchor=(1.01, 0.5), labelcolor=INK2)
    fig.suptitle("Jellyfish do not float like oil: even at 0.1 m/s an open boom keeps only 60-80% for 30 min",
                 x=0.01, ha="left", fontsize=12, fontweight="bold", color=INK)
    fig.tight_layout()
    fig.savefig(OUT / "fig5_boom_retention.png", dpi=160)
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

    rows = hold_grid()
    results["curtain_hold"] = rows
    fig_hold(rows)
    fig_paths()

    options = plan_release(cond=cond, record=record)
    results["release_options"] = [o.__dict__ | {"score": round(o.score, 3), "load_factor": round(o.load_factor, 2)} for o in options]
    fig_release(options, src)

    brows = boom_grid()
    results["boom_retention"] = brows
    fig_boom(brows)

    a = results["alerts"][0]
    event_h = (a["window_end_h"] - a["switch_on_h"]) + 12
    results["compressor"] = compressor(curtain_m=GAP_W, airflow_l_s_m=3.0, depth_m=float(site["gap_depth_m"]),
                                       hours=round(event_h))
    retained = run_boom(RELATIVE_TOW, DESIGN_SKIRT, 0.10, 0.25, minutes=30, n=600).retained_share
    results["boom"] = boom_throughput(tow_m_s=RELATIVE_TOW, retention=round(retained, 2))
    results["mass_kg"] = {f"{d} cm": round(jellyfish_mass_kg(d), 2) for d in (30, 45)}

    (OUT / "results.json").write_text(json.dumps(results, indent=2, default=float))
    print(f"Forcing: {src}")
    print(json.dumps({k: results[k] for k in ("alerts", "compressor", "boom")}, indent=2, default=float))
    print("best release:", results["release_options"][0])


if __name__ == "__main__":
    main()
