"""Order-of-magnitude sizing: curtain air and power, boom throughput."""
import numpy as np

P_ATM = 101_325.0  # Pa
RHO_SEA = 1025.0
G = 9.81


def compressor(curtain_m=300.0, airflow_l_s_m=3.0, depth_m=8.0, line_loss_bar=0.5,
               efficiency=0.6, hours=24.0, usd_per_kwh=0.10):
    """Free-air demand and shaft power, isothermal compression with an overall efficiency."""
    q = curtain_m * airflow_l_s_m / 1000.0  # m^3/s free air
    p2 = P_ATM + RHO_SEA * G * depth_m + line_loss_bar * 1e5
    p_ideal_kw = P_ATM * q * np.log(p2 / P_ATM) / 1000.0
    p_kw = p_ideal_kw / efficiency
    return {
        "curtain_m": curtain_m,
        "airflow_l_s_m": airflow_l_s_m,
        "air_m3_s": round(q, 3),
        "air_m3_min": round(q * 60, 1),
        "discharge_bar_abs": round(p2 / 1e5, 2),
        "power_kw": round(p_kw, 0),
        "event_hours": hours,
        "energy_mwh_per_event": round(p_kw * hours / 1000.0, 2),
        "energy_cost_usd_per_event": round(p_kw * hours * usd_per_kwh, 0),
    }


def boom_throughput(mouth_m=30.0, layer_m=1.0, tow_m_s=0.3, density_per_m3=(0.1, 0.5, 1.0),
                    mass_kg=2.0, retention=0.8):
    """Jellyfish swept per hour by one U-boom moving through a swarm.

    retention = share not escaping under the skirt (assumed until tank tests).
    """
    swept_m3_s = mouth_m * layer_m * tow_m_s
    rows = []
    for d in density_per_m3:
        per_h = swept_m3_s * d * 3600 * retention
        rows.append({"density_per_m3": d, "jellyfish_per_h": round(per_h, -2),
                     "tonnes_per_h": round(per_h * mass_kg / 1000.0, 1)})
    return {"swept_m3_s": swept_m3_s, "retention": retention, "mass_kg": mass_kg, "rows": rows}


def jellyfish_mass_kg(bell_cm):
    """Yang et al. (2018): m = 0.08 * D^2.77 g, D in cm (fitted on 2-20 cm animals)."""
    return 0.08 * bell_cm ** 2.77 / 1000.0
