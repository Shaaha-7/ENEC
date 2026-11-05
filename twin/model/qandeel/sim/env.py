"""Coastal environment for the Qandeel drift simulations.

Frame: x = east (m), y = north (m). The coast is the line y = 0 with the sea
at y > 0, and the breakwater gap (the intake entrance) sits at the origin.

Every number here is an assumption stated in the project overview, not site
data. Replace the defaults with Copernicus Marine values for a real site.
"""
from dataclasses import dataclass, replace

import numpy as np

TIDAL_PERIOD_H = 12.42  # principal lunar semi-diurnal tide (M2)
# Extra wind drift on top of the ocean-model current, as a share of wind speed. The 3% rule is for
# oil and floating objects; SMOC currents already include wind-driven (Ekman) and wave (Stokes)
# drift, and jellyfish sit below the surface, so the base case is 1%. 0% and 3% are run as bounds.
WIND_DRIFT_FACTOR = 0.01


@dataclass(frozen=True)
class Conditions:
    """Ocean and weather state driving surface drift."""

    tide_amp: float = 0.30  # along-shore tidal current amplitude, m/s
    tide_phase_h: float = 0.0  # hours after slack water at t = 0
    residual_u: float = 0.08  # residual (non-tidal) current, east component, m/s
    residual_v: float = 0.0  # residual current, north component, m/s
    wind_speed: float = 6.0  # m/s
    wind_from_deg: float = 315.0  # meteorological: direction the wind blows FROM (315 = NW, shamal)

    def wind_drift(self) -> np.ndarray:
        """Surface drift (m/s) caused by the wind; blows toward from + 180 deg."""
        to_rad = np.deg2rad(self.wind_from_deg + 180.0)
        s = WIND_DRIFT_FACTOR * self.wind_speed
        return np.array([s * np.sin(to_rad), s * np.cos(to_rad)])

    def tide(self, t_h: np.ndarray | float) -> np.ndarray | float:
        """Along-shore (east) tidal current at time t (hours)."""
        return self.tide_amp * np.sin(2 * np.pi * (t_h + self.tide_phase_h) / TIDAL_PERIOD_H)


def velocity(cond: Conditions, t_h: float) -> np.ndarray:
    """Spatially uniform surface drift velocity (m/s) at time t_h."""
    w = cond.wind_drift()
    return np.array([cond.residual_u + cond.tide(t_h) + w[0], cond.residual_v + w[1]])


def perturb(cond: Conditions, rng: np.random.Generator) -> Conditions:
    """One plausible variant of the forecast, for Monte Carlo spread.

    Forecast errors: +-20% current strength, +-30% wind speed, +-20 deg wind
    direction, and an unknown tide phase within +-1 h.
    """
    return replace(
        cond,
        tide_amp=cond.tide_amp * rng.normal(1.0, 0.2),
        tide_phase_h=cond.tide_phase_h + rng.uniform(-1.0, 1.0),
        residual_u=cond.residual_u * rng.normal(1.0, 0.2) + rng.normal(0, 0.02),
        residual_v=cond.residual_v + rng.normal(0, 0.02),
        wind_speed=max(0.0, cond.wind_speed * rng.normal(1.0, 0.3)),
        wind_from_deg=cond.wind_from_deg + rng.normal(0, 20.0),
    )


def bearing_to_xy(distance_km: float, bearing_deg: float) -> np.ndarray:
    """Position (m) of a point at a distance and compass bearing from the gap."""
    b = np.deg2rad(bearing_deg)
    return 1000.0 * distance_km * np.array([np.sin(b), np.cos(b)])
