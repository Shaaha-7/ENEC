"""Run every Qandeel simulation and write figures plus a results summary.

    python -m qandeel.run_all            # writes qandeel/outputs/

All inputs are stated assumptions (see qandeel/README.md); outputs demonstrate
the concept, they are not site predictions.
"""
import json
from pathlib import Path

import matplotlib

matplotlib.use("Agg")
import matplotlib.pyplot as plt  # noqa: E402
import numpy as np  # noqa: E402

from qandeel.sim.curtain import (CURTAIN_Y, GAP_W, bulson_surface_current, hold_grid,  # noqa: E402
                                 run_hold)
from qandeel.sim.env import Conditions, bearing_to_xy  # noqa: E402
from qandeel.sim.planning import (ARRIVE_RADIUS_M, approach_section, forecast_arrival,  # noqa: E402
                                  plan_release, switch_on_time)
from qandeel.sim.sizing import boom_throughput, compressor, jellyfish_mass_kg  # noqa: E402

OUT = Path(__file__).parent / "outputs"

# Palette: one quiet grey for context, one blue accent, ordered blues for airflow levels.
INK, INK2, MUTED, GRID = "#0b0b0b", "#52514e", "#898781", "#e4e3df"
GREY, ACCENT = "#b9b8b2", "#2a78d6"
BLUES = {0.0: GREY, 1.5: "#86b6ef", 3.0: "#2a78d6", 4.5: "#104281"}
SURFACE = "#fcfcfb"

ALERTS = [  # (label, distance km, bearing deg)
    ("Swarm 28 km W-NW", 28, 285),
    ("Swarm 28 km N", 28, 0),
]


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


def fig_arrival(fc, t_on, label):
    fig, (ax1, ax2) = plt.subplots(1, 2, figsize=(11, 4.6), facecolor=SURFACE,
                                   gridspec_kw={"width_ratios": [1, 1.2]})
    for tr in fc.tracks:
        ax1.plot(tr[:, 0] / 1000, tr[:, 1] / 1000, color=GREY, lw=0.8, alpha=0.6)
    ax1.axhline(0, color=INK2, lw=1.5)
    ax1.add_patch(plt.Circle((0, 0), ARRIVE_RADIUS_M / 1000, color=ACCENT, alpha=0.25))
    ax1.plot([0], [0], marker="v", color=ACCENT, ms=9)
    s = fc.tracks[0][0] / 1000
    ax1.plot([s[0]], [s[1]], "o", color=INK, ms=6)
    ax1.annotate("alert position", s, xytext=(6, 6), textcoords="offset points", fontsize=9, color=INK)
    ax1.annotate("intake gap", (0, 0), xytext=(8, -14), textcoords="offset points", fontsize=9, color=ACCENT)
    ax1.text(0.98, 0.02, "coast", transform=ax1.transAxes, ha="right", fontsize=9, color=INK2)
    ax1.set_xlabel("km east of gap", color=INK2, fontsize=9)
    ax1.set_ylabel("km offshore", color=INK2, fontsize=9)
    ax1.set_xlim(-32, 45)
    ax1.set_ylim(-1.5, 14)
    _style(ax1, f"{label}: 40 of {fc.runs} drift paths")

    hrs = fc.arrival_hours[~np.isnan(fc.arrival_hours)]
    ax2.hist(hrs, bins=np.arange(0, 97, 3), color=GREY, edgecolor=SURFACE, linewidth=1)
    ax2.axvspan(fc.p10_h, fc.p90_h, color=ACCENT, alpha=0.10)
    ax2.axvline(fc.p50_h, color=ACCENT, lw=2)
    ax2.axvline(t_on, color=INK, lw=1.5, ls="--")
    top = ax2.get_ylim()[1]
    ax2.text(t_on, top * 0.95, f" curtain on: {t_on:.0f} h", fontsize=9, color=INK, ha="right")
    ax2.text(fc.p50_h, top * 0.85, f" median {fc.p50_h:.0f} h", fontsize=9, color=ACCENT)
    ax2.text(fc.p90_h, top * 0.70, f" 10-90%: {fc.p10_h:.0f}-{fc.p90_h:.0f} h", fontsize=9, color=INK2)
    ax2.set_xlabel("hours after alert until the swarm reaches the gap", color=INK2, fontsize=9)
    ax2.set_ylabel("simulated runs", color=INK2, fontsize=9)
    _style(ax2, f"{fc.p_arrive:.0%} of runs reach the gap; curtain switches on at {t_on:.0f} h",
           "Monte Carlo drift: tide, residual current, 3% wind drift, eddies, forecast error")
    fig.tight_layout(w_pad=4)
    fig.savefig(OUT / "fig1_arrival_window.png", dpi=160)
    plt.close(fig)


def fig_hold(rows):
    fig, ax = plt.subplots(figsize=(10.5, 4.6), facecolor=SURFACE)
    for q in sorted({r["airflow_l_s_m"] for r in rows}):
        rr = [r for r in rows if r["airflow_l_s_m"] == q]
        x = [r["approach_m_s"] for r in rr]
        y = [100 * r["held_share"] for r in rr]
        name = "curtain off" if q == 0 else f"{q:g} L/s per m (surface current {bulson_surface_current(q):.2f} m/s)"
        ax.plot(x, y, color=BLUES[q], lw=2, marker="o", ms=6, label=name)
    ax.axvspan(0.0, 0.10, color=GRID, alpha=0.6)
    ax.text(0.052, 4, "typical approach\nflow at a sheltered\nintake gap (assumed)", fontsize=8, color=INK2)
    ax.set_xlim(0.03, 0.42)
    ax.set_ylim(-3, 105)
    ax.set_xlabel("approach current toward the gap (m/s)", color=INK2, fontsize=9)
    ax.set_ylabel("jellyfish held outside after 3 h (%)", color=INK2, fontsize=9)
    ax.legend(frameon=False, fontsize=8.5, loc="center left", bbox_to_anchor=(1.01, 0.5),
              labelcolor=INK2, title="bubble airflow", title_fontsize=9)
    _style(ax, "At 3 L/s per m the curtain holds the swarm up to 0.2 m/s; above ~0.3 m/s it fails",
           "400 simulated jellyfish per point, each swimming 2-10 cm/s on a wandering heading")
    fig.tight_layout()
    fig.savefig(OUT / "fig2_curtain_hold.png", dpi=160)
    plt.close(fig)


def fig_paths():
    fig, axes = plt.subplots(1, 2, figsize=(11, 4.4), facecolor=SURFACE, sharey=True)
    for ax, q, title in ((axes[0], 0.0, "Curtain off: {:.0%} enter the gap in 3 h"),
                         (axes[1], 3.0, "Curtain on (3 L/s per m): {:.0%} enter")):
        res = run_hold(0.15, q, n=400, seed=3)
        half = GAP_W / 2
        ax.plot([-600, -half], [0, 0], color=INK2, lw=4, solid_capstyle="butt")
        ax.plot([half, 600], [0, 0], color=INK2, lw=4, solid_capstyle="butt")
        if q:
            ax.plot([-half, half], [CURTAIN_Y, CURTAIN_Y], color=ACCENT, lw=2, ls=(0, (2, 2)))
            ax.text(-half + 4, CURTAIN_Y + 14, "bubble curtain", ha="left", fontsize=9, color=ACCENT)
        start = res.start_xy
        ax.scatter(start[:, 0], start[:, 1], s=6, color=GREY, alpha=0.5, lw=0)
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
    axes[1].text(0.98, 0.95, "grey = starting positions\nblack = still outside after 3 h,\nbunched for the boom to collect",
                 transform=axes[1].transAxes, ha="right", va="top", fontsize=8.5, color=INK2)
    fig.tight_layout()
    fig.savefig(OUT / "fig3_curtain_positions.png", dpi=160)
    plt.close(fig)


def fig_release(options):
    fig, ax = plt.subplots(figsize=(8.5, 4.8), facecolor=SURFACE)
    ax.axhline(0, color=INK2, lw=1.5)
    ax.plot([0], [0], marker="v", color=INK, ms=9)
    ax.annotate("intake gap", (0, 0), xytext=(6, -14), textcoords="offset points", fontsize=9, color=INK)
    for o in options:
        x, y = bearing_to_xy(o.distance_km, o.bearing_deg) / 1000
        ax.scatter([x], [y], s=60, color=GREY if o.score > 0.05 else ACCENT, zorder=3)
        ax.annotate(f"{o.p_return:.0%}", (x, y), xytext=(0, 7), textcoords="offset points",
                    ha="center", fontsize=7.5, color=INK2)
    best = options[0]
    bx, by = bearing_to_xy(best.distance_km, best.bearing_deg) / 1000
    ax.annotate(f"chosen: {best.distance_km:g} km, {best.bearing_deg:.0f} deg\n{best.tow_hours:.0f} h tow at 0.3 m/s",
                (bx, by), xytext=(-215, -42), textcoords="offset points", fontsize=9, color=ACCENT,
                arrowprops={"arrowstyle": "-", "color": ACCENT})
    ax.set_aspect("equal", adjustable="datalim")
    ax.set_xlabel("km east of gap", color=INK2, fontsize=9)
    ax.set_ylabel("km offshore", color=INK2, fontsize=9)
    _style(ax, "Releasing 10 km down-current gives no returns in 72 h",
           "Label = share of simulated releases drifting back to the gap; blue = no returns and no strandings near the plant")
    fig.tight_layout()
    fig.savefig(OUT / "fig4_release_points.png", dpi=160)
    plt.close(fig)


def main():
    OUT.mkdir(exist_ok=True)
    cond = Conditions()
    results = {"conditions": cond.__dict__, "alerts": []}

    for i, (label, dist, bearing) in enumerate(ALERTS):
        fc = forecast_arrival(dist, bearing, cond, runs=400, seed=i)
        t_on = switch_on_time(fc)
        results["alerts"].append({
            "label": label, "distance_km": dist, "bearing_deg": bearing,
            "p_arrive": round(fc.p_arrive, 3),
            "arrival_p10_h": fc.p10_h and round(fc.p10_h, 1),
            "arrival_p50_h": fc.p50_h and round(fc.p50_h, 1),
            "arrival_p90_h": fc.p90_h and round(fc.p90_h, 1),
            "switch_on_h": t_on and round(t_on, 1),
            "section": approach_section(fc) if fc.p_arrive else None,
        })
        if i == 0:
            fig_arrival(fc, t_on, label)

    rows = hold_grid()
    results["curtain_hold"] = rows
    fig_hold(rows)
    fig_paths()

    options = plan_release(cond)
    results["release_options"] = [o.__dict__ | {"score": round(o.score, 3)} for o in options]
    fig_release(options)

    a = results["alerts"][0]
    event_h = (a["arrival_p90_h"] - a["switch_on_h"]) + 12  # run until the late tail passes, plus margin
    results["compressor"] = compressor(curtain_m=GAP_W, airflow_l_s_m=3.0, hours=round(event_h))
    results["boom"] = boom_throughput()
    results["mass_kg"] = {f"{d} cm": round(jellyfish_mass_kg(d), 2) for d in (30, 45)}

    (OUT / "results.json").write_text(json.dumps(results, indent=2, default=float))
    print(json.dumps({k: results[k] for k in ("alerts", "compressor", "boom", "mass_kg")}, indent=2,
                     default=float))
    print("best release:", results["release_options"][0])


if __name__ == "__main__":
    main()
