# Tank test: build and measure the curtain and boom at 1:20

Goal: replace the simulation's three biggest guesses with measurements, using
kit that costs under about AED 300. The sensitivity analysis
(`qandeel/outputs/fig6_sensitivity.png`) says what matters most:

1. how fast an approach current the bubble curtain can hold back,
2. how much water dives under the boom at its apex (the "downflow fraction"),
3. how much a closed-bottom retention bag cuts that loss.

Model sizes come from `python -m qandeel.tank_scale --scale 20` (Froude scaling:
lengths / 20, speeds / 4.5, air per metre of pipe / 89). Jellyfish behaviour does
not scale, so the mock jellyfish are passive: this tests the water physics, not
swimming. Live-animal tests come later, with ENEC and a permit.

## What a 1:20 tank can and cannot tell us

Use the tank to **compare setups** (curtain on vs off, more vs less air, open boom vs
bag) and to measure the **downflow fraction** and **bag leakage** ratios. Do not read
its numbers as a prediction of full-scale performance:

- **Bubbles do not scale.** Aquarium bubbles are millimetre-sized and rise at
  0.2-0.3 m/s, about the same as full-size bubbles, so relative to the scaled flow they
  rise roughly 4.5 times too fast. Small bubble barriers are known to behave differently
  from full-size ones. Treat the tank curtain as a qualitative check of the model trend.
- **One slice of the gap.** A 1 m air line represents a 20 m slice of a 300 m opening.
  The way the flow converges on the gap is not reproduced.
- **Uneven flow.** A pump in a tub makes swirling, uneven flow. Measure the approach
  speed at several points and report the spread; a long narrow trough or a slow
  stream-table (flume) is better than a round pool.
- **Short runs.** Test 2 ideally needs about 18 m of tow. In a short tank, tow back and
  forth and count jellyfish after each pass, or hold the boom still in the pump's
  current instead of towing it (same speed through the water).
- **No depth for the curtain model.** The simulation treats the flow as flat. Release
  mock jellyfish at three depths (near the surface, mid-depth, near the bottom) to see
  whether deeper ones are drawn under the curtain, which the model cannot show.

## Kit

| Item | Use | Approx. cost |
|---|---|---|
| Long storage tub or kids' pool, water 45-50 cm deep | the tank (1:20 of 9.5 m depth) | 60-120 AED |
| Aquarium air pump, 3-5 L/min, with a valve | air supply (model needs ~2 L/min per metre) | 40-80 AED |
| 1 m of airline tubing pierced every 1 cm with a hot needle, or a 60 cm air-stone bar | the bubble curtain | 10-30 AED |
| Small submersible pump (300-600 L/h) | creates the approach current | 40-60 AED |
| Mock jellyfish: 2 cm discs of agar or gelatine set in a bottle cap, tinted | neutrally buoyant, soft, visible | 15 AED |
| Salt | tune the tank water until the discs hover, neither float nor sink | 5 AED |
| Pool noodle + plastic sheet + coins | model boom: noodle float, 10 cm sheet skirt, coins as ballast | 20 AED |
| Fine fabric pocket 15 cm deep (old T-shirt) | retention bag at the boom apex | 0 |
| Peppercorns or confetti, phone camera on a tripod, tape measure | flow tracers and video | 0-20 AED |

## Test 1: curtain hold

1. Lay the pierced tubing across the tank 3-5 cm above the bottom, perpendicular to the flow.
2. Run the submersible pump at one end to make a steady current toward the curtain.
3. Measure the approach current: time a tracer over 50 cm without the curtain (repeat 3 times).
4. Release 30 mock jellyfish 40 cm upstream, at mid-depth and near the surface. Count how many cross the curtain in 2 minutes (= 9 min full-scale).
5. Repeat at 3 pump settings (aim for about 3, 4.5 and 7 cm/s, i.e. 0.13, 0.20 and 0.31 m/s full-scale) and 2 air settings (about 1 and 2 L/min per metre).
6. Film from above; also measure the bubble surface current with tracers released at the curtain.

Record in a table: approach speed, airflow, surface current, number crossed out of 30.
Compare with `fig2_curtain_hold.png`: the model says nearly all are held at 4.5 cm/s and most cross at 8-9 cm/s.

## Test 2: boom and retention bag

1. Make a 150 cm U-boom from the noodle with a 10 cm skirt; tie a string to each end.
2. Place 30 mock jellyfish ahead of it and pull the boom slowly along the tank at a steady speed (mark distances on the tub; time with a phone).
3. Test speeds of about 2.2, 4.5 and 6.7 cm/s (0.1, 0.2 and 0.3 m/s full-scale) for 6-7 minutes each (30 min full-scale), or as long as the tank allows, turning at the ends.
4. Count mocks still in the boom. Then add the 15 cm fabric bag at the apex and repeat.
5. Drop a little food colouring at the apex while towing and film from the side: the dye shows the downflow. Measure how fast it sinks against the tow speed. That ratio is the "downflow fraction" the model guesses at 0.25-0.5.

Compare with `fig5_boom_retention.png`: open boom should lose most mocks above ~4.5 cm/s; the bag should keep almost all.

## What to send back

A table per test, the phone videos, and the measured downflow ratio. Put the
numbers in the simulation (`DOWNFLOW_FRACTION` and `BAG_LEAK` in
`qandeel/sim/boom.py`) and re-run `python -m qandeel.run_all`. Even one evening's
results turn "the model says" into "we measured".
