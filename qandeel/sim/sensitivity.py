"""One-at-a-time sensitivity: which unknowns move the key results most.

Each unknown is set to a low and a high plausible value while everything else
stays at the base case. The spread tells the team what to measure first.
"""
from .boom import run_boom
from .curtain import run_hold
from .sizing import compressor

BASE_APPROACH = 0.20  # m/s, a demanding but realistic approach current at the gap


def _hold(**kw):
    args = dict(approach_speed=BASE_APPROACH, airflow=3.0, n=300, swim_max=0.10)
    args.update(kw)
    return 100 * run_hold(**args).held_share


def _boom(**kw):
    args = dict(tow_speed=0.20, skirt_m=2.0, swim_max=0.10, downflow_fraction=0.25, minutes=30, n=300, bag=True)
    args.update(kw)
    return 100 * run_boom(**args).retained_share


def _power(**kw):
    args = dict(curtain_m=300.0, airflow_l_s_m=3.0, depth_m=9.5)
    args.update(kw)
    return compressor(**args)["power_kw"]


def tornado():
    """Rows: outcome, unknown, low label, low value, high label, high value, base value."""
    out = []
    base = _hold()
    for name, lo, hi in [
        ("Approach current at the gap", dict(approach_speed=0.10), dict(approach_speed=0.30)),
        ("Jellyfish top swim speed", dict(swim_max=0.05), dict(swim_max=0.20)),
        ("Bubble airflow (L/s per m)", dict(airflow=1.5), dict(airflow=4.5)),
        ("Water depth at gap", dict(depth=6.0), dict(depth=12.0)),
    ]:
        out.append({"outcome": "Curtain: % held outside the gap", "unknown": name,
                    "low": next(iter(lo.values())), "low_value": round(_hold(**lo), 1),
                    "high": next(iter(hi.values())), "high_value": round(_hold(**hi), 1), "base": round(base, 1)})
    base = _boom()
    for name, lo, hi in [
        ("Apex downflow fraction", dict(downflow_fraction=0.15), dict(downflow_fraction=0.5)),
        ("Bag fabric leak", dict(bag=True), dict(bag=True)),  # placeholder, replaced below
        ("Jellyfish top swim speed", dict(swim_max=0.05), dict(swim_max=0.20)),
        ("Tow speed through water (m/s)", dict(tow_speed=0.10), dict(tow_speed=0.30)),
    ]:
        if name == "Bag fabric leak":
            from . import boom as b
            keep = b.BAG_LEAK
            b.BAG_LEAK = 0.1
            lv = _boom()
            b.BAG_LEAK = 0.4
            hv = _boom()
            b.BAG_LEAK = keep
            out.append({"outcome": "Boom + bag: % kept for 30 min", "unknown": name, "low": 0.1,
                        "low_value": round(lv, 1), "high": 0.4, "high_value": round(hv, 1), "base": round(base, 1)})
            continue
        out.append({"outcome": "Boom + bag: % kept for 30 min", "unknown": name,
                    "low": next(iter(lo.values())), "low_value": round(_boom(**lo), 1),
                    "high": next(iter(hi.values())), "high_value": round(_boom(**hi), 1), "base": round(base, 1)})
    base = _power()
    for name, lo, hi in [
        ("Gap width (m)", dict(curtain_m=150.0), dict(curtain_m=600.0)),
        ("Bubble airflow (L/s per m)", dict(airflow_l_s_m=2.4), dict(airflow_l_s_m=3.6)),
        ("Water depth at gap (m)", dict(depth_m=6.0), dict(depth_m=12.0)),
    ]:
        out.append({"outcome": "Compressor power (kW)", "unknown": name,
                    "low": next(iter(lo.values())), "low_value": _power(**lo),
                    "high": next(iter(hi.values())), "high_value": _power(**hi), "base": base})
    return out
