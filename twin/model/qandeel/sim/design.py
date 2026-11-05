"""Implementation design numbers for the bubble curtain, boom, bag and boats.

    python -m qandeel.sim.design      # prints and writes qandeel/outputs/design.json

Every number is either an input stated below (with its source) or a standard engineering
formula applied to it. Values marked ASSUMED must be confirmed by survey, vendor data or the
sea pilot. Nothing here replaces a detailed design by a marine/pipeline engineer.
"""
import json
import math
from pathlib import Path

from .forcing import load_site

OUT = Path(__file__).resolve().parent.parent / "outputs"
SITE = load_site()

RHO_SEA = 1025.0  # kg/m3
G = 9.81
P_ATM = 101_325.0  # Pa
R_AIR = 287.0  # J/(kg K)
T_AIR = 318.0  # K: 45 C summer air after the aftercooler (ASSUMED)

GAP_M = float(SITE["gap_width_m"])  # 967 m, measured between the breakwater tips
DEPTH_M = float(SITE["gap_depth_m"])  # 9.5 m, public description, NOT surveyed
SECTIONS = 10
AIR_L_S_M = {"normal": 3.0, "maximum": 4.5}  # from the curtain model (Bulson-type relation)
DIFFUSER_HEIGHT_M = 0.75  # pipe held 0.5-1 m above the bed so sand cannot bury it
MAIN_LENGTH_M = 3000.0  # compressor house at the breakwater root to the valve chamber at the tip
DARCY_F = 0.015  # smooth HDPE/steel at these Reynolds numbers (ASSUMED, check with pipe engineer)

# Boom: Desmi RO-BOOM 2000 data sheet (1.10 m skirt, 0.60 m freeboard, 50/100/200 m sections)
BOOM_SKIRT_M, BOOM_FREEBOARD_M = 1.10, 0.60
BOOM_LENGTH_M = 200.0  # four 50 m sections
U_OPENING_RATIO = 0.33  # common oil-response rule of thumb: U opening ~1/3 of boom length
TOW_M_S = 0.20  # through the water, from the boom model
CD_BOOM = (1.2, 2.0)  # drag coefficient range for a flexible skirt (ASSUMED range)

# Bag: 10 t of jellyfish at ~seawater density, at most half full (from the herding model)
BAG_T = 10.0
JELLY_DENSITY_T_M3 = 1.02  # jellyfish are ~96% water (near seawater density)


def pressure_budget(q_free_m3_s, d_main_m=0.5):
    """Compressor discharge pressure (bar gauge) as the sum of its parts."""
    p_water = RHO_SEA * G * (DEPTH_M - DIFFUSER_HEIGHT_M)
    p_abs = P_ATM + p_water + 1.5e5 * 0  # placeholder for iteration below
    # air density in the main at roughly discharge pressure (iterate once)
    p_guess = P_ATM + p_water + 0.3e5
    rho_air = p_guess / (R_AIR * T_AIR)
    q_act = q_free_m3_s * P_ATM / p_guess
    v = q_act / (math.pi * d_main_m ** 2 / 4)
    dp_main = DARCY_F * MAIN_LENGTH_M / d_main_m * rho_air * v ** 2 / 2
    parts = {"water_column_bar": p_water / 1e5, "diffuser_bar": (0.03, 0.09),
             "main_pipe_bar": dp_main / 1e5, "valves_fittings_margin_bar": (0.10, 0.20),
             "main_velocity_m_s": v}
    lo = parts["water_column_bar"] + 0.03 + parts["main_pipe_bar"] + 0.10
    hi = parts["water_column_bar"] + 0.09 + parts["main_pipe_bar"] + 0.20
    parts["discharge_bar_g"] = (round(lo, 2), round(hi, 2))
    del p_abs
    return parts


def section_design(air_l_s_m, hole_d_mm=2.0, hole_dp_bar=0.05, cd=0.62):
    """One ~97 m diffuser section: air, feed-pipe size, hole count, buoyancy and ballast."""
    sec_len = GAP_M / SECTIONS
    q_free = sec_len * air_l_s_m / 1000.0  # m3/s free air
    p_abs = P_ATM + RHO_SEA * G * (DEPTH_M - DIFFUSER_HEIGHT_M) + hole_dp_bar * 1e5
    rho_air = p_abs / (R_AIR * T_AIR)
    q_act = q_free * P_ATM / p_abs
    # feed/diffuser pipe: keep air velocity <= 10 m/s for even distribution along the pipe
    d_req = math.sqrt(4 * q_act / (math.pi * 10.0))
    hole_area = math.pi * (hole_d_mm / 1000) ** 2 / 4
    v_hole = math.sqrt(2 * hole_dp_bar * 1e5 / rho_air)
    q_hole = cd * hole_area * v_hole
    holes = q_act / q_hole
    # buoyancy of an air-filled 200 mm OD pipe and the ballast to hold it down (safety factor 1.5)
    od = 0.200
    uplift_n_m = RHO_SEA * G * math.pi * od ** 2 / 4
    return {"section_length_m": round(sec_len, 1), "air_free_m3_s": round(q_free, 3),
            "air_free_m3_min": round(q_free * 60, 1), "air_actual_m3_s_at_depth": round(q_act, 3),
            "pipe_id_required_mm": round(d_req * 1000), "pipe_choice": "HDPE PE100 OD200 SDR17 (ID ~176 mm), sized for the maximum airflow",
            "hole_d_mm": hole_d_mm, "hole_pressure_drop_bar": hole_dp_bar, "holes_per_section": round(holes),
            "holes_per_m": round(holes / sec_len, 1), "hole_spacing_cm": round(100 * sec_len / holes, 1),
            "uplift_kn_per_section": round(uplift_n_m * sec_len / 1000, 1),
            "ballast_submerged_t_per_section": round(1.5 * uplift_n_m * sec_len / G / 1000, 1)}


def plume():
    """Bubble plume rise speed and how far a cross-current bends it (order of magnitude)."""
    out = {}
    for k, q in AIR_L_S_M.items():
        u0 = 1.46 * (G * q / 1000) ** (1 / 3)  # Bulson-type surface current, as in the curtain model
        for cross in (0.14, 0.23):
            # plume rises at roughly the same order as u0; lateral drift = cross * rise time
            rise_s = (DEPTH_M - DIFFUSER_HEIGHT_M) / u0
            out[f"{k}_cross_{cross}"] = {"surface_current_m_s": round(u0, 2), "rise_time_s": round(rise_s),
                                         "plume_shift_m": round(cross * rise_s, 1)}
    return out


def boom_and_boats():
    opening = BOOM_LENGTH_M * U_OPENING_RATIO
    area = opening * BOOM_SKIRT_M  # projected underwater area across the flow
    bag_w, bag_d = 4.0, 3.0
    bag_area = bag_w * bag_d
    forces = {}
    for v in (TOW_M_S, 0.35):
        f = [0.5 * RHO_SEA * cd * (area + bag_area) * v ** 2 / 1000 for cd in CD_BOOM]
        forces[f"{v}_m_s"] = {"total_kn": [round(x, 1) for x in f], "per_boat_kn": [round(x / 2, 1) for x in f]}
    bag_vol = BAG_T / JELLY_DENSITY_T_M3
    big = {}
    for t in (20.0, 30.0):  # larger bags: mouth scales with the square root of volume
        k = math.sqrt(t / BAG_T)
        a = (area + bag_area * k * k)
        big[f"{int(t)}_t_bag"] = {"volume_half_full_m3": round(2 * t / JELLY_DENSITY_T_M3),
                                  "per_boat_kn_at_0.2_m_s": [round(0.5 * RHO_SEA * cd * a * TOW_M_S ** 2 / 2000, 1) for cd in CD_BOOM]}
    return {"bigger_bags": big, "boom": {"model": "Desmi RO-BOOM 2000 class", "skirt_m": BOOM_SKIRT_M, "freeboard_m": BOOM_FREEBOARD_M,
                     "length_m": BOOM_LENGTH_M, "u_opening_m": round(opening), "sections": "4 x 50 m"},
            "bag": {"jellyfish_t": BAG_T, "jellyfish_m3": round(bag_vol, 1), "volume_half_full_m3": round(2 * bag_vol),
                    "mouth_w_m": bag_w, "depth_m": bag_d, "length_m": round(2 * bag_vol / (bag_w * bag_d) * 2, 1),
                    "shape": "tapered sock, mouth 4 m x 3 m, closed bottom, permeable fabric"},
            "tow_force": forces,
            "boat_bollard_pull_t_recommended": "~1-2 t per boat (2-3x the calm-water force at 0.35 m/s, for waves and gusts)"}


def capacity(herding_rows, target_t_day=200.0, gather_t_h=10.0):
    """Units needed to remove target_t_day by boat, for bag size, tug count and release distance."""
    target = target_t_day / 24
    rows = []
    for h in herding_rows:
        kept = 1 - h["p_return"]
        for bag in (10.0, 20.0, 30.0):
            for tugs in (1, 3, 5):
                if tugs == 1:
                    net = bag * kept / (bag / gather_t_h + h["round_trip_h"])
                else:
                    net = min(gather_t_h, tugs * bag / h["round_trip_h"]) * kept
                units = math.ceil(target / net)
                boats = units * (2 + (tugs if tugs > 1 else 0))  # two boats tow each boom, plus shuttle tugs
                rows.append({"release_km": h["distance_km"], "bag_t": bag, "shuttle_tugs": tugs if tugs > 1 else 0,
                             "net_t_h_per_unit": round(net, 2), "units_for_target": units, "boats_for_target": boats})
    return {"target_t_h": round(target, 1), "rows": rows}


def main():
    res = json.loads((OUT / "results.json").read_text())
    q = {k: GAP_M * v / 1000 for k, v in AIR_L_S_M.items()}
    design = {
        "inputs": {"gap_m": GAP_M, "depth_m": DEPTH_M, "depth_surveyed": bool(SITE.get("gap_depth_measured")),
                   "sections": SECTIONS, "diffuser_height_above_bed_m": DIFFUSER_HEIGHT_M},
        "geometry": {"straight_tip_to_tip_m": GAP_M,
                     "seaward_arc_100m_bulge_m": round(_arc_length(GAP_M, 100)),
                     "seaward_arc_200m_bulge_m": round(_arc_length(GAP_M, 200))},
        "air_free_m3_min": {k: round(v * 60) for k, v in q.items()},
        "pressure": {k: pressure_budget(v) for k, v in q.items()},
        "main_pipe_options": {f"DN{int(d * 1000)}": round(pressure_budget(q["maximum"], d)["main_pipe_bar"], 3)
                              for d in (0.4, 0.5, 0.6)},
        "section": {k: section_design(v) for k, v in AIR_L_S_M.items()},
        "plume": plume(),
        "boom_boats": boom_and_boats(),
        "capacity_200t_day": capacity(res["herding"]["rows"]),
    }
    OUT.mkdir(exist_ok=True)
    (OUT / "design.json").write_text(json.dumps(design, indent=2, default=list))
    print(json.dumps(design, indent=2, default=list))


def _arc_length(chord, bulge):
    """Length of a circular arc with this chord and mid-point offset."""
    r = (chord ** 2 / 4 + bulge ** 2) / (2 * bulge)
    theta = 2 * math.asin(chord / (2 * r))
    return r * theta


if __name__ == "__main__":
    main()
