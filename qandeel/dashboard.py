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
import streamlit as st  # noqa: E402

from qandeel.data_fetch import download  # noqa: E402
from qandeel.plan_alert import compass  # noqa: E402
from qandeel.sim.env import bearing_to_xy  # noqa: E402
from qandeel.sim.forcing import load_forecast, load_record, load_site, source_label  # noqa: E402
from qandeel.sim.planning import ARRIVE_RADIUS_M, forecast_arrival, plan_release, switch_on_time  # noqa: E402

INK, INK2, GRID, GREY, ACCENT, SURFACE = "#0b0b0b", "#52514e", "#e4e3df", "#b9b8b2", "#2a78d6", "#fcfcfb"
GOOD, WARN = "#0ca30c", "#fab219"
HORIZON_H = 96  # same horizon as the planner

st.set_page_config(page_title="Qandeel response dashboard", layout="wide")


@st.cache_data(show_spinner="Simulating 400 possible drifts of the swarm...")
def run_plan(distance, bearing, swim, mode, start_hour, data_key=None):
    record = {"forecast": load_forecast, "season": load_record}.get(mode, lambda: None)()
    fc = forecast_arrival(distance, bearing, record=record, runs=400, horizon_h=96, swim_max=swim,
                          start_hour=start_hour)
    t_on = switch_on_time(fc)
    release_hour = start_hour + (fc.p50_h or 0)
    opts = plan_release(record=record, start_hour=release_hour) if t_on is not None else []
    best = opts[0] if opts else None
    return {
        "p_arrive": fc.p_arrive, "share": fc.share_reaching, "p10": fc.p10_h, "p50": fc.p50_h, "p90": fc.p90_h,
        "t_on": t_on, "side": fc.side, "hourly": fc.cloud.hourly[: HORIZON_H + 1], "start": fc.start_xy,
        "release": None if best is None else {
            "km": best.distance_km, "bearing": best.bearing_deg, "p_return": best.p_return,
            "tow_h": best.tow_hours, "tow_p90_h": best.tow_p90_h,
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
        return "ON", GOOD, "Curtain running; uncrewed boats collecting jellyfish held at the gap"
    return "OFF", GREY, "Swarm has passed; curtain off, data logged for the next plan"


def draw_map(p, t):
    fig, ax = plt.subplots(figsize=(8.5, 5.2), facecolor=SURFACE)
    ax.set_facecolor("#eef4fb")
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
    ymax = max(17.0, sy + 3)
    if p["release"]:
        ymax = max(ymax, ry + 3)
    half_w = max(31.0, abs(sx) + 8, ymax * 1.55)
    ax.fill_between([-half_w, half_w], -3, 0, color="#e9e4d6", zorder=1)
    ax.set_xlim(-half_w, half_w)
    ax.set_ylim(-3, ymax)
    ax.set_aspect("equal")
    ax.set_xlabel("km east of the intake gap", color=INK2, fontsize=9)
    ax.set_ylabel("km offshore", color=INK2, fontsize=9)
    ax.tick_params(colors=INK2, labelsize=8)
    ax.text(half_w - 1, ymax - 1, "N\n^", ha="center", va="top", fontsize=10, color=INK2)
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


@st.cache_data(ttl=6 * 3600, show_spinner="Downloading the live current and wind forecast...")
def refresh_forecast(lat, lon):
    """Fetch a fresh 7-day forecast at most every 6 hours (keeps the hosted app live). False if offline."""
    try:
        return download(lat, lon, forecast=True)[1][0]
    except (Exception, SystemExit):
        return False


site = load_site()
refresh_forecast(site["data_lat"], site["data_lon"])
forecast = load_forecast()
modes = {}
if load_record() is not None:
    modes["season"] = "Replay of 2025 bloom season (results in the deck)"
if forecast is not None:
    modes["forecast"] = "Live forecast (operational mode)"
modes["assumed"] = "Assumed conditions"
fc_start = datetime.strptime(forecast.time[0][:16], "%Y-%m-%dT%H:%M") if forecast else None
with st.sidebar:
    st.header("Early-warning alert")
    qp = st.query_params  # e.g. ?d=15&b=330&t=30 for a ready-made demo
    distance = st.number_input("Distance from intake gap (km)", 2.0, 60.0, float(qp.get("d", 15)), 1.0)
    bearing = st.number_input("Bearing from gap (degrees, 0 = north)", 0.0, 359.0, float(qp.get("b", 330)), 5.0)
    st.caption(f"= {compass(bearing)}")
    mode = st.radio("Ocean data", list(modes), format_func=modes.get,
                    help="Download with `python -m qandeel.data_fetch` (season) or `--forecast` (live).")
    if mode == "forecast" and fc_start:
        alert_date = st.date_input("Alert date (UTC)", fc_start.date())
        times = [f"{h:02d}:{m:02d}" for h in range(24) for m in (0, 30)]
        alert_clock = datetime.strptime(st.selectbox("Alert time (UTC)", times, index=fc_start.hour * 2),
                                        "%H:%M").time()
        st.caption(f"Forecast covers {fc_start:%d %b %H:%M} UTC + {len(forecast.time)} h.")
    else:
        alert_date, alert_clock = fc_start.date() if fc_start else datetime(2026, 7, 14).date(), datetime.min.time()
        st.caption("Season replay: each simulated run starts at a random hour of summer 2025, "
                   "so times are shown as hours after the alert, not clock times.")
    swim = st.select_slider("Top jellyfish swim speed", [0.10, 0.15, 0.20], 0.10,
                            format_func=lambda v: f"{v * 100:.0f} cm/s")
    st.divider()
    st.caption(f"Site: {site['site_name']}. Gap {site['gap_width_m']} m "
               f"({'measured' if site['gap_width_measured'] else 'assumed'}).")

alert_at = datetime.combine(alert_date, alert_clock)
clock_mode = mode == "forecast" and fc_start is not None


def when(h):
    return f"{(alert_at + timedelta(hours=h)):%a %H:%M}" if clock_mode else f"+{h:.0f} h"


start_hour = (alert_at - fc_start).total_seconds() / 3600 if (mode == "forecast" and fc_start) else 0.0
if mode == "forecast" and not 0 <= start_hour <= 72:
    st.sidebar.warning("Alert time is outside the forecast; using the forecast start.")
    start_hour = 0.0
p = run_plan(distance, bearing, swim, mode, round(start_hour, 1), fc_start if mode == "forecast" else None)

st.title("Qandeel response dashboard")
st.caption(f"Forcing: {p['source']}. Concept demonstration; operational use needs ENEC detection data and validation.")

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
         f"bulk of swarm at {when(p['p50'])}")
    tile(c3, "Curtain switch-on", when(p["t_on"]), f"{p['t_on']:.0f} h after the alert")
    r = p["release"]
    if r is None:
        tile(c4, "Release point", "none reachable", "the current blocks every candidate tow; hold at the curtain")
    else:
        tile(c4, "Release point", f"{r['km']:g} km {compass(r['bearing'])}",
             f"{r['tow_h']:.0f} h tow over the ground (slow case {r['tow_p90_h']:.0f} h); "
             f"{r['p_return']:.0%} drift back within 72 h and meet the curtain again")
    if clock_mode and r is not None:
        need = start_hour + p["p50"] + r["tow_p90_h"] + 72
        if need > len(forecast.time):
            st.warning(f"Release planning needs about {need:.0f} h of forecast but only {len(forecast.time)} h exist; "
                       "later hours repeat the last forecast hour. Re-plan when the forecast updates.")

if "clock" not in st.session_state:
    st.session_state.clock = int(st.query_params.get("t", 0))  # ?t=30 opens at hour 30
play = st.button(f"Play 0 to {HORIZON_H} h")
clock = st.slider("Clock: hours after alert", 0, HORIZON_H, key="clock")

left, right = st.columns([3, 2])
map_slot, status_slot, line_slot = left.empty(), right.empty(), st.empty()


def render(t):
    status, colour, msg = status_at(p, t)
    map_slot.pyplot(draw_map(p, t), clear_figure=True)
    head = f"{(alert_at + timedelta(hours=t)):%a %d %b, %H:%M}" if clock_mode else f"{t} h after the alert"
    status_slot.markdown(
        f"### {head}\n"
        f"<div style='font-size:28px;font-weight:700;color:{colour}'>Curtain {status}</div>\n\n{msg}\n\n"
        + (f"- Swarm approaches from the **{p['side']}** side: stage the uncrewed boats there\n"
           "- Boom with closed-bottom retention bag, at most 0.2 m/s through the water\n"
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
