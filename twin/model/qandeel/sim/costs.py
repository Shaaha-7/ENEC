"""Itemised capital and running cost of the full Qandeel system, with the basis of every number.

    python -m qandeel.sim.costs      # writes qandeel/outputs/costs.json

Confidence levels:
  A  quantity from our own calculation (design.py), unit rate from a published or derived figure
  B  quantity calculated, unit rate an engineering budget range (no published price found)
  C  placeholder: needs a vendor quote before it can be relied on
No vendor quote has been obtained yet. All prices are budget ranges in USD (2026).
"""
import json
from pathlib import Path

OUT = Path(__file__).resolve().parent.parent / "outputs"


def _items(design, res):
    sec = design["section"]["maximum"]
    # 2 duty units carry the normal airflow (174 m3/min); the standby runs as a third duty unit only in
    # the rare hours above the normal setting (0.4% of 2025 hours in the base case), so 3 units in total.
    n_comp = 3
    kw_each = round(res["compressor"]["power_kw"] * 1.5 / 3)  # maximum airflow is 1.5x normal
    return [
        # (group, item, quantity, unit, unit_low, unit_high, basis, confidence)
        ("Air supply", "Oil-free low-pressure screw compressors, VSD, ~87 m3/min at 1.5 bar(g) "
         "(e.g. Atlas Copco ZE/ZA VSD class), 2 duty + 1 standby/peak",
         n_comp * kw_each, "kW installed", 1000, 2300, "budget USD/kW for packaged oil-free units", "C"),
        ("Air supply", "Compressor house: building, aftercoolers, filters, MV/LV switchgear, transformer",
         1, "lot", 300_000, 800_000, "budget range", "B"),
        ("Air supply", "Air main DN500 along the western breakwater, with supports and valves",
         3000, "m", 300, 700, "budget installed rate for large-bore pipe on a breakwater", "B"),
        ("Curtain", f"Diffuser sections: HDPE OD200 pipe, 2 mm holes, steel frames, ~{sec['ballast_submerged_t_per_section']} t "
         "ballast each, 10 x 97 m", 967, "m", 500, 1500, "budget installed rate (fabricated on land)", "B"),
        ("Curtain", "Feed hoses DN150, one per section, valve chamber to section (~5 km in total)",
         5000, "m", 50, 150, "budget rate for reinforced air hose / HDPE", "B"),
        ("Curtain", "Valve chamber on the western tip: 10 remote valves, pressure sensors, PLC",
         1, "lot", 100_000, 300_000, "budget range", "B"),
        ("Marine works", "Installation: crane barge or multicat + dive team, ~20-40 working days",
         30, "days", 15_000, 30_000, "budget day rate, vessel + divers", "C"),
        ("Marine works", "Survey (bathymetry, seabed, currents, waves), detailed design, permits",
         1, "lot", 200_000, 500_000, "budget range", "B"),
        ("Gathering", "Ocean boom, 1.1 m skirt (Desmi RO-BOOM 2000 class): 2 gathering units x 200 m + "
         "2 tip catch pockets x 100 m", 600, "m", 200, 600, "budget USD/m for heavy offshore boom", "C"),
        ("Gathering", "Custom closed-bottom bags, permeable fabric, quick-release (6 bags)",
         6, "bags", 10_000, 30_000, "budget range, custom fabrication", "C"),
        ("Gathering", "Workboats 8-10 m, >= 2 t bollard pull, slow-speed control (owned: 2)",
         2, "boats", 150_000, 400_000, "budget range for small aluminium workboats", "C"),
        ("Monitoring", "Cameras (mouth + both tips), edge computer for the jellyfish detector, radar/AIS link",
         1, "lot", 60_000, 150_000, "budget range", "B"),
    ]


def _lean_items(design, res):
    """Cost-reduced design. Each change is listed in LEAN_CHANGES with what it depends on."""
    kw_each = round(res["compressor"]["power_kw"] * 1.5 / 3)
    feed_m = 2600  # 5 feed lines, each serving a pair of sections (194 m switching blocks)
    return [
        ("Air supply", "Oil-free screw blowers 0.3-1.5 bar(g), VSD, ISO 8573-1 Class 0 (Atlas Copco ZS / "
         "Aerzen Delta Hybrid class), 2 duty + 1 standby/peak, in ISO containers", 3 * kw_each, "kW installed",
         600, 1500, "budget USD/kW for packaged aeration blowers", "C"),
        ("Air supply", "Containers' foundation on the western tip, small transformer, LV switchgear",
         1, "lot", 150_000, 400_000, "budget range", "B"),
        ("Air supply", "11 kV power cable along the breakwater (instead of a 3 km DN500 air main)",
         3000, "m", 100, 250, "budget installed rate for MV cable in duct", "B"),
        ("Curtain", "Diffuser sections: HDPE OD200 on precast concrete sleepers (support + ballast in one), "
         "float-and-sink installation", 967, "m", 300, 900, "budget installed rate", "B"),
        ("Curtain", "Feed lines DN150, 5 lines each serving 2 sections (~2.6 km in total)",
         feed_m, "m", 50, 150, "budget rate", "B"),
        ("Curtain", "Valve manifold at the tip: 5 remote valves, pressure sensors, PLC",
         1, "lot", 60_000, 180_000, "budget range", "B"),
        ("Marine works", "Installation: multicat + dive team, ~20 working days (sections pre-assembled on land)",
         20, "days", 15_000, 30_000, "budget day rate", "C"),
        ("Marine works", "Survey gaps, detailed design, permits (reusing the operator's existing site surveys)",
         1, "lot", 100_000, 300_000, "budget range", "B"),
        ("Gathering", "Ocean boom 1.1 m skirt: 1 gathering unit 200 m + 2 tip catch pockets x 100 m",
         400, "m", 200, 600, "budget USD/m for heavy offshore boom", "C"),
        ("Gathering", "Custom closed-bottom bags (4)", 4, "bags", 10_000, 30_000, "budget range", "C"),
        ("Monitoring", "Cameras (mouth + both tips), edge computer for the jellyfish detector, radar/AIS link",
         1, "lot", 60_000, 150_000, "budget range", "B"),
    ]


LEAN_CHANGES = [
    ("Screw blowers instead of 4-bar compressors", "our pressure need is 1.06-1.28 bar(g); blowers go to 1.5 bar(g)",
     "the mouth must be <= ~11 m deep (survey)"),
    ("Blowers in containers at the western tip, fed by a power cable", "removes the 3 km DN500 air main and the "
     "compressor building", "The operator confirms space, power route and security rules on the breakwater"),
    ("Concrete sleepers instead of steel frames", "one precast part gives support and ballast", "seabed survey"),
    ("Feed lines in pairs (5 instead of 10)", "curtain switched in 194 m blocks instead of 97 m", "none"),
    ("Shorter marine works (20 days)", "sections built and ballasted on land, float-and-sink", "contractor"),
    ("Reuse existing surveys", "The plant's marine works were surveyed already", "The operator shares data"),
    ("One gathering unit + two tip pockets, no owned boats", "the tide brings the swarm to the pockets; boats "
     "chartered on alert days", "pilot shows the pockets fill"),
]
OPEX_LEAN = [
    ("Operators on call in the bloom season (share of existing plant staff)", 60_000, 150_000, "B"),
    ("Charter 2 workboats on alert days (~40 days a season) + extra boats in large blooms", 240_000, 630_000, "C"),
    ("Diver/ROV inspection and cleaning of diffusers, 4-6 visits", 30_000, 90_000, "B"),
    ("Blower, boom and bag maintenance, spares", 30_000, 80_000, "B"),
]


OPEX = [
    ("Operators on call in the bloom season (share of existing plant staff)", 60_000, 150_000, "B"),
    ("Extra boats chartered during large blooms (8-13 boats, ~10 bloom days a year)", 120_000, 390_000, "C"),
    ("Diver/ROV inspection and cleaning of diffusers, 4-6 visits", 30_000, 90_000, "B"),
    ("Compressor, boom and bag maintenance, spares", 40_000, 100_000, "B"),
]
CONTINGENCY = (0.20, 0.30)

# Value of avoided outage: The plant has 4 units of ~1,400 MW (5.6 GW in total).
UNIT_MW = 1400
VALUE_USD_PER_MWH = (50, 80)  # ASSUMED value of lost generation; to agree with the operator


def build(design, res, items=None, opex=None):
    items = items or _items(design, res)
    opex = opex or OPEX
    rows, lo, hi = [], 0.0, 0.0
    for g, item, q, unit, ul, uh, basis, conf in items:
        a, b = q * ul, q * uh
        lo += a
        hi += b
        rows.append({"group": g, "item": item, "qty": q, "unit": unit, "unit_low": ul, "unit_high": uh,
                     "low_usd": round(a, -3), "high_usd": round(b, -3), "basis": basis, "confidence": conf})
    cont = (lo * CONTINGENCY[0], hi * CONTINGENCY[1])
    capex = (lo + cont[0], hi + cont[1])
    energy = res["cost"]["energy_cost_per_season_usd"]
    op_lo = sum(o[1] for o in opex) + energy
    op_hi = sum(o[2] for o in opex) + energy
    unit_day_mwh = UNIT_MW * 24
    day_value = (unit_day_mwh * VALUE_USD_PER_MWH[0], unit_day_mwh * VALUE_USD_PER_MWH[1])
    pilot = [("Rental oil-free compressor (Atlas Copco ZE rental class), 6 months", 60_000, 150_000),
             ("One 97 m diffuser section on frames + feed hose", 50_000, 150_000),
             ("Installation and removal, dive team", 60_000, 150_000),
             ("Boom 200 m + 2 bags + charter workboat", 60_000, 150_000),
             ("Cameras, sonar, drifters, survival tests, monitoring", 50_000, 120_000)]
    return {
        "note": "Budget estimates without vendor quotes. Confidence A/B/C per item (see module docstring).",
        "capex_items": rows,
        "capex_subtotal_usd": [round(lo, -4), round(hi, -4)],
        "contingency_usd": [round(cont[0], -4), round(cont[1], -4)],
        "capex_total_usd": [round(capex[0], -5), round(capex[1], -5)],
        "opex_items": [{"item": a, "low_usd": b, "high_usd": c, "confidence": d} for a, b, c, d in opex],
        "energy_per_season_usd": energy,
        "opex_total_usd_per_year": [round(op_lo, -4), round(op_hi, -4)],
        "unit_outage_day": {"unit_mw": UNIT_MW, "mwh_per_day": unit_day_mwh,
                            "value_usd": [day_value[0], day_value[1]], "value_basis": "ASSUMED USD 50-80 per MWh"},
        "unit_outage_days_to_repay_capex": [round(capex[0] / day_value[1], 1), round(capex[1] / day_value[0], 1)],
        "unit_outage_days_per_year_to_cover_opex": [round(op_lo / day_value[1], 2), round(op_hi / day_value[0], 2)],
        "pilot_items": [{"item": a, "low_usd": b, "high_usd": c} for a, b, c in pilot],
        "pilot_total_usd": [sum(p[1] for p in pilot), sum(p[2] for p in pilot)],
    }


def main():
    design = json.loads((OUT / "design.json").read_text())
    res = json.loads((OUT / "results.json").read_text())
    c = build(design, res)
    c["lean"] = build(design, res, _lean_items(design, res), OPEX_LEAN)
    c["lean"]["changes"] = [{"change": a, "why": b, "depends_on": d} for a, b, d in LEAN_CHANGES]
    (OUT / "costs.json").write_text(json.dumps(c, indent=2))
    print(json.dumps(c, indent=2))


if __name__ == "__main__":
    main()
