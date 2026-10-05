"""Qandeel operator dashboard: type an alert, see the response unfold.

    streamlit run qandeel/dashboard.py

Uses the real Gulf record (qandeel/data/gulf_forcing.csv) when present,
otherwise assumed conditions. Drag the clock or press Play to animate.
"""
import sys
import time
from datetime import datetime, timedelta
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent.parent))  # allow `streamlit run qandeel/dashboard.py`

import matplotlib.pyplot as plt  # noqa: E402
import numpy as np  # noqa: E402
import streamlit as st  # noqa: E402

from qandeel.plan_alert import compass  # noqa: E402
from qandeel.sim.env import bearing_to_xy  # noqa: E402
from qandeel.sim.forcing import load_record, load_site, source_label  # noqa: E402
from qandeel.sim.planning import ARRIVE_RADIUS_M, forecast_arrival, plan_release, switch_on_time  # noqa: E402

INK, INK2, GRID, GREY, ACCENT, SURFACE = "#0b0b0b", "#52514e", "#e4e3df", "#b9b8b2", "#2a78d6", "#fcfcfb"
GOOD, WARN = "#0ca30c", "#fab219"
HORIZON_H = 72

st.set_page_config(page_title="Qandeel response dashboard", layout="wide")


@st.cache_data(show_spinner="Simulating 400 possible drifts of the swarm...")
def run_plan(distance, bearing, swim, use_real):
    record = load_record() if use_real else None
    fc = forecast_arrival(distance, bearing, record=record, runs=400, horizon_h=96, swim_max=swim)
    t_on = switch_on_time(fc)
    best = plan_release(record=record)[0] if t_on is not None else None
    return {
        "p_arrive": fc.p_arrive, "share": fc.share_reaching, "p10": fc.p10_h, "p50": fc.p50_h, "p90": fc.p90_h,
        "t_on": t_on, "side": fc.side, "hourly": fc.cloud.hourly[: HORIZON_H + 1], "start": fc.start_xy,
        "release": None if best is None else {
            "km": best.distance_km, "bearing": best.bearing_deg, "p_return": best.p_return,
            "tow_h": best.tow_hours,
            "p_near": best.p_beach_near, "xy": bearing_to_xy(best.distance_km, best.bearing_deg)},
        "source": source_label(record),
    }


def fmt_h(h):
    return f"{int(h)} h {int(round((h % 1) * 60)):02d} min"


def status_at(p, t):
    if p["t_on"] is None:
        return "OFF", GREY, "Swarm unlikely to reach the gap. Curtain stays off; keep tracking."
    if t < p["t_on"]:
        return "STANDBY", WARN, f"Curtain switches on in {fmt_h(p['t_on'] - t)}"
    if t < p["p90"] + 12:
        return "ON", GOOD, "Curtain running; boom crew collecting jellyfish held at the gap"
    return "OFF", GREY, "Swarm has passed; curtain off, data logged for the next plan"


def draw_map(p, t):
    fig, ax = plt.subplots(figsize=(8.5, 5.2), facecolor=SURFACE)
    ax.set_facecolor("#eef4fb")
    ax.fill_between([-60, 60], -3, 0, color="#e9e4d6", zorder=1)
    ax.axhline(0, color=INK2, lw=1.5, zorder=2)
    h = int(min(t, HORIZON_H))
    cloud = p["hourly"][h]  # (runs, particles, 2)
    ax.scatter(cloud[1:, :, 0].ravel() / 1000, cloud[1:, :, 1].ravel() / 1000, s=3, color=GREY, alpha=0.35,
               lw=0, zorder=3, label="other possible positions")
    ax.scatter(cloud[0, :, 0] / 1000, cloud[0, :, 1] / 1000, s=14, color=ACCENT, lw=0, zorder=4,
               label="swarm (most likely run)")
    ax.add_patch(plt.Circle((0, 0), ARRIVE_RADIUS_M / 1000, color=INK, fill=False, ls=":", lw=1, zorder=4))
    status, colour, _ = status_at(p, t)
    ax.plot([-0.6, 0.6], [0.15, 0.15], color=colour, lw=4, zorder=5)
    ax.plot([0], [0], marker="v", color=INK, ms=10, zorder=6)
    ax.annotate(f"intake gap  |  curtain {status}", (0, 0), xytext=(10, -16), textcoords="offset points",
                fontsize=9, color=INK, zorder=6)
    if p["release"]:
        rx, ry = p["release"]["xy"] / 1000
        ax.plot([0, rx], [0.4, ry], color=ACCENT, ls="--", lw=1, zorder=3)
        ax.plot([rx], [ry], marker="*", color=ACCENT, ms=16, zorder=6, label="release point")
        ax.annotate(f"release {p['release']['km']:g} km {compass(p['release']['bearing'])}", (rx, ry),
                    xytext=(10, 6), textcoords="offset points", fontsize=9, color=ACCENT)
    sx, sy = p["start"] / 1000
    ax.plot([sx], [sy], marker="x", color=INK, ms=8, zorder=6)
    ax.annotate("alert position", (sx, sy), xytext=(6, 8), textcoords="offset points", fontsize=8.5, color=INK2)
    ax.set_xlim(-36, 26)
    ax.set_ylim(-3, 17)
    ax.set_aspect("equal")
    ax.set_xlabel("km east of the intake gap", color=INK2, fontsize=9)
    ax.set_ylabel("km offshore", color=INK2, fontsize=9)
    ax.tick_params(colors=INK2, labelsize=8)
    ax.text(25, 16, "N\n^", ha="center", va="top", fontsize=10, color=INK2)
    ax.legend(loc="upper left", frameon=False, fontsize=8, labelcolor=INK2)
    for s in ax.spines.values():
        s.set_color(GRID)
    fig.tight_layout()
    return fig


def draw_timeline(p, t):
    fig, ax = plt.subplots(figsize=(12, 1.4), facecolor=SURFACE)
    ax.set_facecolor(SURFACE)
    ax.set_xlim(0, HORIZON_H)
    ax.set_ylim(0, 1)
    ax.set_yticks([])
    for s in ("top", "right", "left"):
        ax.spines[s].set_visible(False)
    ax.tick_params(colors=INK2, labelsize=8)
    if p["t_on"] is not None:
        ax.axvspan(p["p10"], min(p["p90"], HORIZON_H), ymin=0.25, ymax=0.75, color=ACCENT, alpha=0.15)
        ax.text(p["p10"], 0.82, "arrival window", fontsize=8, color=ACCENT)
        ax.axvline(p["t_on"], color=INK, lw=1.5, ls="--")
        ax.text(p["t_on"], 0.05, " curtain on", fontsize=8, color=INK)
        ax.axvline(p["p50"], color=ACCENT, lw=2)
    ax.axvline(t, color="#d03b3b", lw=2.5)
    ax.text(t, 0.55, " now", fontsize=8, color="#d03b3b")
    ax.set_xlabel("hours after alert", color=INK2, fontsize=8)
    fig.tight_layout()
    return fig


site = load_site()
has_real = load_record() is not None
with st.sidebar:
    st.header("Early-warning alert")
    distance = st.number_input("Distance from intake gap (km)", 2.0, 60.0, 28.0, 1.0)
    bearing = st.number_input("Bearing from gap (degrees, 0 = north)", 0.0, 359.0, 285.0, 5.0)
    st.caption(f"= {compass(bearing)}")
    alert_date = st.date_input("Alert date", datetime(2026, 7, 14))
    times = [f"{h:02d}:{m:02d}" for h in range(24) for m in (0, 30)]
    alert_clock = datetime.strptime(st.selectbox("Alert time", times, index=12), "%H:%M").time()
    swim = st.select_slider("Top jellyfish swim speed", [0.10, 0.15, 0.20], 0.10,
                            format_func=lambda v: f"{v * 100:.0f} cm/s")
    use_real = st.toggle("Use real Gulf currents and wind", value=has_real, disabled=not has_real,
                         help="Run `python -m qandeel.data_fetch` on your computer to download them.")
    st.divider()
    st.caption(f"Site: {site['site_name']}. Gap {site['gap_width_m']} m "
               f"({'measured' if site['gap_width_measured'] else 'assumed'}).")

p = run_plan(distance, bearing, swim, use_real)
alert_at = datetime.combine(alert_date, alert_clock)

st.title("Qandeel response dashboard")
st.caption(f"Forcing: {p['source']}. Simulation of the concept, not an operational forecast.")

c1, c2, c3, c4 = st.columns(4)
def tile(col, label, value, note=""):
    col.metric(label, value)
    if note:
        col.caption(note)


tile(c1, "Chance swarm reaches gap", f"{p['p_arrive']:.0%}", f"about {p['share']:.0%} of the swarm")
if p["t_on"] is None:
    tile(c2, "Arrival window", "not expected")
    tile(c3, "Curtain switch-on", "stays off")
    tile(c4, "Release point", "not needed")
else:
    tile(c2, "Arrival window", f"{p['p10']:.0f}-{p['p90']:.0f} h",
         f"bulk of swarm at {(alert_at + timedelta(hours=p['p50'])):%a %H:%M}")
    tile(c3, "Curtain switch-on", f"{(alert_at + timedelta(hours=p['t_on'])):%a %H:%M}",
         f"{p['t_on']:.0f} h after the alert")
    r = p["release"]
    tile(c4, "Release point", f"{r['km']:g} km {compass(r['bearing'])}",
         f"{r['tow_h']:.0f} h tow; {r['p_return']:.0%} drift back within 72 h and meet the curtain again")

if "clock" not in st.session_state:
    st.session_state.clock = int(st.query_params.get("t", 0))  # ?t=30 opens at hour 30
play = st.button("Play 0 to 72 h")
clock = st.slider("Clock: hours after alert", 0, HORIZON_H, key="clock")

left, right = st.columns([3, 2])
map_slot, status_slot, line_slot = left.empty(), right.empty(), st.empty()


def render(t):
    status, colour, msg = status_at(p, t)
    map_slot.pyplot(draw_map(p, t), clear_figure=True)
    now = alert_at + timedelta(hours=t)
    status_slot.markdown(
        f"### {now:%a %d %b, %H:%M}\n"
        f"<div style='font-size:28px;font-weight:700;color:{colour}'>Curtain {status}</div>\n\n{msg}\n\n"
        + (f"- Swarm approaches from the **{p['side']}** side: stage the boom crew there\n"
           "- Boom drifts with the current, at most 0.1 m/s through the water\n"
           "- Gap camera switches the curtain on at once if jellyfish arrive early"
           if p["t_on"] is not None else ""),
        unsafe_allow_html=True)
    line_slot.pyplot(draw_timeline(p, t), clear_figure=True)
    plt.close("all")


if play:
    for t in range(0, HORIZON_H + 1, 2):
        render(t)
        time.sleep(0.25)
else:
    render(clock)
