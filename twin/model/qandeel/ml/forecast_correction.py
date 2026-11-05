"""Learning model that corrects the physics drift forecast, tested on synthetic events.

    python -m qandeel.ml.forecast_correction        # writes qandeel/outputs/ml_forecast_correction.json + fig8

What it shows: once real events exist (alert -> did the swarm arrive, and when), a small
machine-learning model can learn the site's systematic forecast errors and correct the
physics planner. No real Barakah events are available yet, so the "real sea" here is a
stand-in: the same 2025 Copernicus currents and ERA5 wind, plus a HIDDEN SITE BIAS that the
planner does not know about, plus per-event noise. The bias values below are invented for
the demonstration; with real data the model would learn whatever the real bias is.

Events are split by date: the model learns from June to mid-August and is tested on
mid-August to September, as it would learn from past events and be used on new ones.
"""
import json
from dataclasses import replace
from pathlib import Path

import matplotlib

matplotlib.use("Agg")
import matplotlib.pyplot as plt  # noqa: E402
import numpy as np  # noqa: E402
from sklearn.ensemble import GradientBoostingClassifier, GradientBoostingRegressor  # noqa: E402
from sklearn.linear_model import LogisticRegression  # noqa: E402
from sklearn.metrics import brier_score_loss, roc_auc_score  # noqa: E402

from qandeel.sim.benefits import HORIZON_H  # noqa: E402
from qandeel.sim.env import bearing_to_xy  # noqa: E402
from qandeel.sim.forcing import load_record  # noqa: E402
from qandeel.sim.planning import forecast_arrival, simulate_cloud  # noqa: E402

OUT = Path(__file__).resolve().parent.parent / "outputs"

# Hidden site bias of the stand-in "real sea" (invented for this test, unknown to the planner).
BIAS_CURRENT_SCALE = 1.25  # the ocean model underestimates local currents by 25%
BIAS_ONSHORE_M_S = 0.015  # a steady 1.5 cm/s drift toward the coast the model misses
BIAS_WIND_SCALE = 1.6  # jellyfish feel 1.6% wind drift, not the planner's 1%
# Per-event noise on top (weather the forecast gets wrong on the day).
NOISE_CURRENT = (0.85, 1.15)
NOISE_EXTRA_M_S = 0.01

LEAD_N = 3  # same 5% rule as the planner (3 of 60 particles)


def _truth(record, d, b, s, rng):
    k = rng.uniform(*NOISE_CURRENT)
    ang = rng.uniform(0, 2 * np.pi)
    extra = rng.uniform(0, NOISE_EXTRA_M_S) * np.array([np.sin(ang), np.cos(ang)])
    onshore = np.array([0.0, -BIAS_ONSHORE_M_S])  # north is offshore
    rec = replace(record, kind="forecast",
                  current=record.current * BIAS_CURRENT_SCALE * k + onshore + extra,
                  wind_drift=record.wind_drift * BIAS_WIND_SCALE)
    c = simulate_cloud(bearing_to_xy(d, b), HORIZON_H, rng, runs=1, n_particles=60, record=rec,
                       keep_runs=0, start_hour=s, forecast_error=False)
    arr = np.sort(c.arrive_h[0])
    arr = arr[~np.isnan(arr)]
    return (True, float(arr[LEAD_N - 1])) if arr.size >= LEAD_N else (False, np.nan)


def _conditions(record, s, hours=48):
    """Forecast conditions over the next 48 h (known at alert time): mean current and wind drift."""
    i, j = int(s), int(s) + hours
    cur = record.current[i:j].mean(axis=0)
    wind = record.wind_drift[i:j].mean(axis=0)
    return [cur[0], cur[1], wind[0], wind[1], np.hypot(*record.current[i:j].T).mean()]


def make_events(n=600, seed=11):
    record = load_record()
    rec_fc = replace(record, kind="forecast")
    rng = np.random.default_rng(seed)
    n_h = len(record.time)
    rows = []
    for _ in range(n):
        s = float(rng.integers(0, n_h - int(HORIZON_H) - 50))
        d = float(rng.uniform(10, 35))
        b = float(rng.uniform(-90, 90)) % 360
        fc = forecast_arrival(d, b, record=rec_fc, start_hour=s, runs=150, n_particles=30,
                              seed=int(rng.integers(1e9)))
        arrived, t_first = _truth(record, d, b, s, rng)
        lead = fc.lead_h[~np.isnan(fc.lead_h)]
        rows.append({"start_hour": s, "distance_km": d, "bearing_deg": b,
                     "p_arrive": fc.p_arrive, "share_reaching": fc.share_reaching,
                     "p10_h": fc.p10_h, "p50_h": fc.p50_h,
                     "lead_med_h": float(np.median(lead)) if lead.size else None,
                     "cond": _conditions(record, s), "arrived": arrived, "first_h": t_first})
    return rows


def _features(rows):
    X = []
    for r in rows:
        b = np.radians(r["bearing_deg"])
        p10 = r["p10_h"] if r["p10_h"] is not None else HORIZON_H
        X.append([r["distance_km"], np.sin(b), np.cos(b), r["p_arrive"], r["share_reaching"], p10, *r["cond"]])
    return np.array(X)


def _logit(p):
    p = np.clip(p, 0.01, 0.99)
    return np.log(p / (1 - p))


def evaluate(rows, n_train=None, seed=0):
    """Compare the physics planner with two corrections learned from past events:
    'recalibrated' (a simple statistical fix, works with few events) and
    'learning model' (gradient-boosted trees on forecast + conditions, needs more events)."""
    rows = sorted(rows, key=lambda r: r["start_hour"])
    split = int(0.6 * len(rows))
    train, test = rows[:split], rows[split:]
    if n_train is not None:
        train = train[-n_train:]  # the most recent events, as a site would accumulate them
    Xtr, Xte = _features(train), _features(test)
    ytr = np.array([r["arrived"] for r in train])
    yte = np.array([r["arrived"] for r in test])
    p_tr = np.array([r["p_arrive"] for r in train])
    phys = np.array([r["p_arrive"] for r in test])

    out = {"n_train": len(train), "n_test": len(test), "arrived_test": int(yte.sum())}
    probs = {"physics": phys}
    if len(set(ytr)) > 1:
        cal = LogisticRegression(C=1.0).fit(_logit(p_tr)[:, None], ytr)
        probs["recalibrated"] = cal.predict_proba(_logit(phys)[:, None])[:, 1]
        gb = GradientBoostingClassifier(n_estimators=150, max_depth=2, learning_rate=0.05, subsample=0.8,
                                        random_state=seed).fit(Xtr, ytr)
        probs["ml"] = gb.predict_proba(Xte)[:, 1]
    else:
        probs["recalibrated"] = probs["ml"] = np.full(len(test), ytr.mean())
    for k, p in probs.items():
        out[f"{k}_brier"] = float(brier_score_loss(yte, p))
        if len(set(yte)) > 1:
            out[f"{k}_auc"] = float(roc_auc_score(yte, p))
        on = p >= 0.10  # the planner's switch-on threshold
        out[f"{k}_missed"] = int((yte & ~on).sum())
        out[f"{k}_false_alarms"] = int((~yte & on).sum())

    # Arrival time of the swarm's leading edge: physics median vs learned corrections.
    def timed(rs, xs):
        return [(x, r) for x, r in zip(xs, rs) if r["arrived"] and r["lead_med_h"] is not None]
    tr_t, te_t = timed(train, Xtr), timed(test, Xte)
    if te_t:
        truth = np.array([r["first_h"] for _, r in te_t])
        phys_t = np.array([r["lead_med_h"] for _, r in te_t])
        times = {"physics": phys_t}
        if len(tr_t) >= 3:
            resid = np.array([r["first_h"] - r["lead_med_h"] for _, r in tr_t])
            times["recalibrated"] = phys_t + np.median(resid)
        if len(tr_t) >= 8:
            reg = GradientBoostingRegressor(n_estimators=150, max_depth=2, learning_rate=0.05, subsample=0.8,
                                            loss="absolute_error", random_state=seed)
            reg.fit(np.array([x for x, _ in tr_t]), resid)
            times["ml"] = phys_t + reg.predict(np.array([x for x, _ in te_t]))
        out["n_time_train"], out["n_time_test"] = len(tr_t), len(te_t)
        for k, t in times.items():
            out[f"{k}_time_mae_h"] = float(np.mean(np.abs(t - truth)))
            out[f"{k}_time_bias_h"] = float(np.mean(t - truth))
    return out


def learning_curve(rows, sizes=(10, 20, 40, 80, 160, 360), repeats=5):
    curve = []
    for n in sizes:
        res = [evaluate(rows, n_train=n, seed=k) for k in range(repeats)]
        row = {"n_train": n}
        for k in ("physics", "recalibrated", "ml"):
            for m in ("brier", "time_mae_h"):
                vals = [r[f"{k}_{m}"] for r in res if f"{k}_{m}" in r]
                row[f"{k}_{m}"] = float(np.mean(vals)) if vals else None
        curve.append(row)
    return curve


def figure(curve):
    fig, axes = plt.subplots(1, 2, figsize=(11.5, 4.4), facecolor="#fcfcfb")
    n = [c["n_train"] for c in curve]
    panels = ((axes[0], "brier", "Will it arrive? (error score, lower is better)", "Brier score"),
              (axes[1], "time_mae_h", "When does it arrive? (average error)", "hours"))
    for ax, m, title, unit in panels:
        ax.axhline(curve[-1][f"physics_{m}"], color="#898781", lw=2, ls="--", label="physics planner alone")
        for k, c, lab in (("recalibrated", "#86b6ef", "+ simple recalibration"),
                          ("ml", "#2a78d6", "+ learning model")):
            pts = [(x[ "n_train"], x[f"{k}_{m}"]) for x in curve if x[f"{k}_{m}"] is not None]
            ax.plot(*zip(*pts), "o-", color=c, lw=2.5, label=lab)
        ax.set_xscale("log")
        ax.set_xticks(n)
        ax.set_xticklabels([str(v) for v in n])
        ax.set_xlabel("past alerts learned from")
        ax.set_ylabel(unit)
        ax.set_title(title, loc="left", fontsize=11.5, fontweight="bold")
        ax.grid(color="#e4e3df")
        for sp in ("top", "right"):
            ax.spines[sp].set_visible(False)
    axes[0].legend(frameon=False)
    fig.suptitle("Synthetic test: learning from past alerts corrects a hidden site bias",
                 x=0.01, ha="left", fontsize=13, fontweight="bold")
    fig.tight_layout()
    fig.savefig(OUT / "fig8_ml_forecast_correction.png", dpi=170)
    plt.close(fig)


def main():
    cache = OUT / "ml_events.json"
    if cache.exists():
        rows = json.loads(cache.read_text())
    else:
        rows = make_events()
        cache.write_text(json.dumps(rows))
    full = evaluate(rows)
    curve = learning_curve(rows)
    result = {"note": "Synthetic demonstration: the 'real sea' is the 2025 record plus an invented hidden "
                      "site bias; not a result on real Barakah events.",
              "hidden_bias": {"current_scale": BIAS_CURRENT_SCALE, "onshore_m_s": BIAS_ONSHORE_M_S,
                              "wind_scale": BIAS_WIND_SCALE},
              "events": len(rows), "arrived": int(sum(r["arrived"] for r in rows)),
              "full_split": full, "learning_curve": curve}
    OUT.mkdir(exist_ok=True)
    (OUT / "ml_forecast_correction.json").write_text(json.dumps(result, indent=2))
    figure(curve)
    print(json.dumps(result, indent=2))


if __name__ == "__main__":
    main()
