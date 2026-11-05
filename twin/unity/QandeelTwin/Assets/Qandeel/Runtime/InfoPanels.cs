using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
using UnityEngine.UIElements;

namespace Qandeel
{
    /// <summary>
    /// Forecast, AI camera, Cost, Equipment and Evidence panels. Every number is read from the kit's files at runtime
    /// (forecast_meta.json + fc_atlas_*.png, ai/detections.json + images, detector_metrics.json, costs.json "lean")
    /// or is a verified fact from PROMPT.md section 5. Nothing is invented.
    /// </summary>
    public sealed class InfoPanels
    {
        // ---------- data shapes (JsonUtility)
        [Serializable] public class Grid { public float x0, x1, y0, y1; public int nx, ny; }
        [Serializable] public class Atlas { public int frames, cols; }
        [Serializable] public class Alert
        {
            public string label; public float distance_km, bearing_deg, p_arrive, window_start_h, bulk_arrival_h, window_end_h, switch_on_h;
            public string approach_side; public float share_reaching; public float[] start_km, runs_arrived_by_h, swarm_arrived_by_h;
            public Grid grid; public Atlas atlas;
        }
        [Serializable] class ForecastMeta { public Alert[] alerts; }
        [Serializable] class Pred { public string cls; public float conf; public float[] xyxy; }
        [Serializable] class Det { public string file; public Pred[] pred; }
        [Serializable] class Dets { public Det[] items; }
        [Serializable] class ClassMetrics { public float precision, recall, mAP50, mAP50_95; }
        [Serializable] class Metrics { public string split; public ClassMetrics jellyfish; }
        [Serializable] class CapexItem { public string group, item; public float low_usd, high_usd; public string confidence; }
        [Serializable] class Outage { public float[] value_usd; }
        [Serializable] class Lean
        {
            public CapexItem[] capex_items; public float[] capex_total_usd, contingency_usd, opex_total_usd_per_year, pilot_total_usd,
                unit_outage_days_to_repay_capex; public Outage unit_outage_day;
        }
        [Serializable] class Costs { public Lean lean; }

        readonly VisualElement host;
        readonly Dictionary<string, VisualElement> pages = new Dictionary<string, VisualElement>();
        readonly Dictionary<string, Button> tabs = new Dictionary<string, Button>();
        public string Open { get; private set; } = "";

        // forecast state
        Alert[] alerts; Texture2D[] atlases; int alertIdx, hour; bool playing; float playAcc;
        VisualElement map, curve; Slider slider; Label hourLabel, fcStats, fcLive; Button playBtn;
        readonly List<Button> alertBtns = new List<Button>();

        public InfoPanels(VisualElement root, VisualElement tabRow, TextAsset forecastMeta, Texture2D[] atlas, TextAsset detections,
                          Texture2D[] aiImages, TextAsset metrics, TextAsset costs)
        {
            host = new VisualElement(); host.AddToClassList("info"); root.Add(host);
            var close = new Button(() => Show("")) { text = "✕  Back to live 3D" }; close.AddToClassList("btn"); close.AddToClassList("info__close");
            host.Add(close);
            atlases = atlas;
            AddPage(tabRow, "Forecast", BuildForecast(forecastMeta));
            AddPage(tabRow, "AI camera", BuildAI(detections, aiImages, metrics));
            AddPage(tabRow, "Cost", BuildCost(costs));
            AddPage(tabRow, "Equipment", BuildEquipment());
            AddPage(tabRow, "Evidence", BuildEvidence());
            Show("");
        }

        void AddPage(VisualElement tabRow, string name, VisualElement page)
        {
            page.AddToClassList("info__page"); host.Add(page); pages[name] = page;
            var b = new Button(() => Show(Open == name ? "" : name)) { text = name };
            b.AddToClassList("btn"); b.AddToClassList("tab"); tabRow.Add(b); tabs[name] = b;
        }

        public void Show(string name)
        {
            Open = name;
            host.style.display = name == "" ? DisplayStyle.None : DisplayStyle.Flex;
            host.parent?.EnableInClassList("root--info", name != "");
            foreach (var kv in pages) kv.Value.style.display = kv.Key == name ? DisplayStyle.Flex : DisplayStyle.None;
            foreach (var kv in tabs) kv.Value.EnableInClassList("btn--on", kv.Key == name);
        }

        public void Update(float dt)
        {
            if (Open != "Forecast" || !playing || alerts == null) return;
            playAcc += dt * 8f; // 8 forecast hours per second
            if (playAcc >= 1f) { int n = (int)playAcc; playAcc -= n; SetHour((hour + n) % 97); }
        }

        // ---------- Forecast
        VisualElement BuildForecast(TextAsset metaText)
        {
            var page = new VisualElement();
            page.Add(Title("Drift forecast after an alert", "400 simulated drifts × 60 jellyfish on real 2025 Copernicus currents and ERA5 wind (planning.py)"));
            alerts = JsonUtility.FromJson<ForecastMeta>(metaText.text).alerts;
            var row = new VisualElement(); row.AddToClassList("row"); page.Add(row);
            for (int i = 0; i < alerts.Length; i++)
            {
                int k = i; var b = new Button(() => SetAlert(k)) { text = alerts[i].label }; b.AddToClassList("btn"); b.AddToClassList("btn--seg");
                row.Add(b); alertBtns.Add(b);
            }
            var body = new VisualElement(); body.AddToClassList("fc"); page.Add(body);
            map = new VisualElement(); map.AddToClassList("fc__map"); body.Add(map);
            map.generateVisualContent += DrawMapOverlay;
            var side = new VisualElement(); side.AddToClassList("fc__side"); body.Add(side);
            fcStats = new Label(); fcStats.AddToClassList("fc__stats"); side.Add(fcStats);
            fcLive = new Label(); fcLive.AddToClassList("fc__live"); side.Add(fcLive);
            curve = new VisualElement(); curve.AddToClassList("fc__curve"); side.Add(curve);
            curve.generateVisualContent += DrawCurve;
            var cap = new Label("Share of runs in which ≥5% of the swarm has reached the mouth (2 km radius), by hour"); cap.AddToClassList("note"); side.Add(cap);

            var scrub = new VisualElement(); scrub.AddToClassList("row"); page.Add(scrub);
            playBtn = new Button(() => playing = !playing) { text = "▶" }; playBtn.AddToClassList("btn"); scrub.Add(playBtn);
            slider = new Slider(0, 96) { value = 0 }; slider.AddToClassList("fc__slider");
            slider.RegisterValueChangedCallback(e => SetHour(Mathf.RoundToInt(e.newValue)));
            scrub.Add(slider);
            hourLabel = new Label(); hourLabel.AddToClassList("fc__hour"); scrub.Add(hourLabel);
            SetAlert(0);
            return page;
        }

        void SetAlert(int i)
        {
            alertIdx = i; var a = alerts[i];
            for (int k = 0; k < alertBtns.Count; k++) alertBtns[k].EnableInClassList("btn--on", k == i);
            bool on = a.p_arrive >= 0.10f; // planning.MIN_ARRIVAL_PROB
            fcStats.text =
                $"<b>{a.p_arrive * 100:F0}%</b> chance the swarm arrives\n" +
                (on ? $"Window <b>{a.window_start_h:F0}–{a.window_end_h:F0} h</b> · bulk at <b>{a.bulk_arrival_h:F0} h</b>\n" +
                      $"Curtain on at <b>+{a.switch_on_h:F0} h</b> · approach from the <b>{a.approach_side}</b>"
                    : "Below the 10% threshold: the curtain stays off");
            SetHour(hour);
        }

        void SetHour(int h)
        {
            hour = Mathf.Clamp(h, 0, 96);
            if (Mathf.RoundToInt(slider.value) != hour) slider.SetValueWithoutNotify(hour);
            var a = alerts[alertIdx];
            hourLabel.text = $"+{hour} h";
            float swarm = a.swarm_arrived_by_h != null && hour < a.swarm_arrived_by_h.Length ? a.swarm_arrived_by_h[hour] : 0f;
            float runs = a.runs_arrived_by_h != null && hour < a.runs_arrived_by_h.Length ? a.runs_arrived_by_h[hour] : 0f;
            fcLive.text = $"At +{hour} h: {runs * 100:F0}% of runs arrived · {swarm * 100:F1}% of all jellyfish at the mouth";
            // atlas tile: 80 x 60 px per hour, 10 columns, north up
            var tex = atlases[alertIdx];
            int col = hour % a.atlas.cols, row = hour / a.atlas.cols, rows = Mathf.CeilToInt(a.atlas.frames / (float)a.atlas.cols);
            map.style.backgroundImage = new StyleBackground(tex);
            map.style.backgroundSize = new BackgroundSize(Length.Percent(100f * a.atlas.cols), Length.Percent(100f * rows));
            map.style.backgroundPositionX = new BackgroundPosition(BackgroundPositionKeyword.Left, new Length(-col * MapW, LengthUnit.Pixel));
            map.style.backgroundPositionY = new BackgroundPosition(BackgroundPositionKeyword.Top, new Length(-row * MapH, LengthUnit.Pixel));
            map.MarkDirtyRepaint(); curve.MarkDirtyRepaint();
        }

        const float MapW = 640f, MapH = 480f;

        void DrawMapOverlay(MeshGenerationContext ctx)
        {
            var a = alerts[alertIdx]; var g = a.grid; var p = ctx.painter2D;
            Vector2 P(float xkm, float ykm) => new Vector2((xkm - g.x0) / (g.x1 - g.x0) * MapW, (g.y1 - ykm) / (g.y1 - g.y0) * MapH);
            // model coast (y = 0) and arrival circle (2 km) at the mouth
            p.strokeColor = new Color(0.95f, 0.8f, 0.55f, 0.9f); p.lineWidth = 3f;
            p.BeginPath(); p.MoveTo(P(g.x0, 0.2f)); p.LineTo(P(g.x1, 0.2f)); p.Stroke();
            p.strokeColor = new Color(0.33f, 0.84f, 0.89f); p.lineWidth = 2.5f;
            p.BeginPath(); p.Arc(P(0, 0), 2f / (g.x1 - g.x0) * MapW, 180f, 360f); p.Stroke();
            // alert start
            if (a.start_km != null && a.start_km.Length == 2)
            {
                p.fillColor = new Color(1f, 0.55f, 0.24f); p.BeginPath(); p.Arc(P(a.start_km[0], a.start_km[1]), 7f, 0, 360); p.Fill();
            }
            p.fillColor = new Color(0.33f, 0.84f, 0.89f); p.BeginPath(); p.Arc(P(0, 0), 6f, 0, 360); p.Fill();
        }

        void DrawCurve(MeshGenerationContext ctx)
        {
            var a = alerts[alertIdx]; var r = curve.contentRect; var p = ctx.painter2D;
            if (a.runs_arrived_by_h == null || r.width < 10) return;
            p.strokeColor = new Color(1f, 1f, 1f, 0.15f); p.lineWidth = 1f;
            p.BeginPath(); p.MoveTo(new Vector2(0, r.height)); p.LineTo(new Vector2(r.width, r.height)); p.Stroke();
            p.strokeColor = new Color(0.33f, 0.84f, 0.89f); p.lineWidth = 3f; p.BeginPath();
            int n = a.runs_arrived_by_h.Length;
            for (int i = 0; i < n; i++)
            {
                var pt = new Vector2(i / (float)(n - 1) * r.width, r.height - a.runs_arrived_by_h[i] / Mathf.Max(0.01f, Max(a.runs_arrived_by_h)) * r.height * 0.9f);
                if (i == 0) p.MoveTo(pt); else p.LineTo(pt);
            }
            p.Stroke();
            float x = hour / 96f * r.width;
            p.strokeColor = new Color(1f, 0.55f, 0.24f); p.lineWidth = 2f;
            p.BeginPath(); p.MoveTo(new Vector2(x, 0)); p.LineTo(new Vector2(x, r.height)); p.Stroke();
        }

        static float Max(float[] v) { float m = 0; foreach (var x in v) m = Mathf.Max(m, x); return m; }

        // ---------- AI camera
        VisualElement BuildAI(TextAsset detText, Texture2D[] images, TextAsset metricsText)
        {
            var page = new VisualElement();
            var m = JsonUtility.FromJson<Metrics>(metricsText.text).jellyfish;
            page.Add(Title("AI camera: real detections", "YOLO11n on held-out test images (not used in training) · jellyfish class"));
            var kpis = new VisualElement(); kpis.AddToClassList("row"); page.Add(kpis);
            kpis.Add(Kpi("RECALL", $"{m.recall * 100:F0}%")); kpis.Add(Kpi("mAP50", $"{m.mAP50 * 100:F0}%"));
            kpis.Add(Kpi("PRECISION", $"{m.precision * 100:F0}%")); kpis.Add(Kpi("TEST IMAGES", "116"));
            var dets = JsonUtility.FromJson<Dets>("{\"items\":" + detText.text + "}").items;
            var grid = new VisualElement(); grid.AddToClassList("ai__grid"); page.Add(grid);
            for (int i = 0; i < dets.Length && i < images.Length; i++)
            {
                var cell = new VisualElement(); cell.AddToClassList("ai__cell");
                cell.style.backgroundImage = new StyleBackground(images[i]);
                foreach (var pr in dets[i].pred)
                {
                    if (pr.xyxy == null || pr.xyxy.Length < 4) continue;
                    var box = new VisualElement(); box.AddToClassList("ai__box");
                    box.style.left = Length.Percent(pr.xyxy[0] * 100); box.style.top = Length.Percent(pr.xyxy[1] * 100);
                    box.style.width = Length.Percent((pr.xyxy[2] - pr.xyxy[0]) * 100); box.style.height = Length.Percent((pr.xyxy[3] - pr.xyxy[1]) * 100);
                    var l = new Label($"{pr.conf:F2}"); l.AddToClassList("ai__conf"); box.Add(l);
                    cell.Add(box);
                }
                grid.Add(cell);
            }
            page.Add(Note("Boxes: model predictions with confidence. Shown on real images only; no simulated detections."));
            return page;
        }

        // ---------- Cost (lean design)
        VisualElement BuildCost(TextAsset costText)
        {
            var page = new VisualElement();
            var c = JsonUtility.FromJson<Costs>(costText.text).lean;
            page.Add(Title("Cost and payback (lean design)", "Budget estimates without vendor quotes · costs.json → lean"));
            var kpis = new VisualElement(); kpis.AddToClassList("row"); page.Add(kpis);
            kpis.Add(Kpi("BUILD", $"USD {M(c.capex_total_usd[0])}–{M(c.capex_total_usd[1])} M"));
            kpis.Add(Kpi("RUN / YEAR", $"USD {M(c.opex_total_usd_per_year[0])}–{M(c.opex_total_usd_per_year[1])} M"));
            kpis.Add(Kpi("SEA PILOT", $"USD {M(c.pilot_total_usd[0])}–{M(c.pilot_total_usd[1])} M"));
            kpis.Add(Kpi("PAYS BACK IN", $"{c.unit_outage_days_to_repay_capex[0]:F1}–{c.unit_outage_days_to_repay_capex[1]:F1} unit-days"));
            var table = new ScrollView(); table.AddToClassList("table"); page.Add(table);
            foreach (var it in c.capex_items)
            {
                var r = new VisualElement(); r.AddToClassList("table__row");
                var a = new Label(it.group); a.AddToClassList("table__g"); r.Add(a);
                var b = new Label(it.item); b.AddToClassList("table__item"); r.Add(b);
                var v = new Label($"{K(it.low_usd)}–{K(it.high_usd)}"); v.AddToClassList("table__v"); r.Add(v);
                var cf = new Label(it.confidence); cf.AddToClassList("table__c"); r.Add(cf);
                table.Add(r);
            }
            page.Add(Note($"Contingency included (USD {M(c.contingency_usd[0])}–{M(c.contingency_usd[1])} M). One unit-day of lost output ≈ USD " +
                          $"{M(c.unit_outage_day.value_usd[0])}–{M(c.unit_outage_day.value_usd[1])} M (ASSUMED USD 50–80 per MWh). Confidence A/B/C per item."));
            return page;
        }

        static string M(float usd) => (usd / 1e6f).ToString(usd >= 1e6f ? "0.0" : "0.00", CultureInfo.InvariantCulture);
        static string K(float usd) => usd >= 1e6f ? $"{usd / 1e6f:0.0} M" : $"{usd / 1e3f:0} k";

        // ---------- Equipment (verified dimensions)
        VisualElement BuildEquipment()
        {
            var page = new VisualElement();
            page.Add(Title("Equipment", "Dimensions from the design files · 1.8 m person for scale"));
            var list = new VisualElement(); list.AddToClassList("eq"); page.Add(list);
            void Item(string name, string spec, float lengthM)
            {
                var r = new VisualElement(); r.AddToClassList("eq__row");
                var t = new VisualElement(); t.AddToClassList("eq__text");
                var n = new Label(name); n.AddToClassList("eq__name"); t.Add(n);
                var s = new Label(spec); s.AddToClassList("note"); t.Add(s);
                r.Add(t);
                // log-scale size bar against a 1.8 m person
                var bar = new VisualElement(); bar.AddToClassList("eq__bar");
                var fill = new VisualElement(); fill.AddToClassList("eq__fill");
                fill.style.width = Length.Percent(Mathf.Clamp01(Mathf.Log10(lengthM / 0.1f) / Mathf.Log10(200f / 0.1f)) * 100f);
                bar.Add(fill);
                var person = new VisualElement(); person.AddToClassList("eq__person");
                person.style.left = Length.Percent(Mathf.Log10(1.8f / 0.1f) / Mathf.Log10(200f / 0.1f) * 100f);
                bar.Add(person);
                var len = new Label(lengthM >= 1 ? $"{lengthM:0.#} m" : $"{lengthM * 100:0} cm"); len.AddToClassList("eq__len"); bar.Add(len);
                r.Add(bar); list.Add(r);
            }
            Item("Curtain section ×10", "HDPE PE100 OD200 SDR17 pipe, 2 mm holes ~8.5 cm apart (~1,133 per section), 0.75 m above the seabed on precast concrete sleepers, ~4.7 t submerged ballast", 96.7f);
            Item("Ocean boom (RO-BOOM 2000 class)", "200 m (4 × 50 m), 1.1 m skirt, 0.6 m freeboard, ~66 m U-opening", 200f);
            Item("Closed-bottom bag", "Permeable fabric, mouth 4 × 3 m, ~20 m³ for 10 t", 4f);
            Item("Blower container ×3", "40 ft container, oil-free screw blower 0.3–1.5 bar(g), ~87 m³/min; 2 duty + 1 standby on the western tip, 11 kV cable", 12.19f);
            Item("Workboat (crewed, chartered)", "8–10 m, 1–2 t bollard pull; tows the bag at 0.2 m/s", 9f);
            Item("Blue Blubber (Catostylus mosaicus)", "Domed bell 30–45 cm, no marginal tentacles, 8 frilly oral arms, ~1–3 kg", 0.38f);
            page.Add(Note("Bars on a log scale from 10 cm to 200 m; the white tick is a 1.8 m person. Sleeper spacing is not specified in the design."));
            return page;
        }

        // ---------- Evidence
        VisualElement BuildEvidence()
        {
            var page = new VisualElement();
            page.Add(Title("Evidence: what is shown and what is not yet known", "The curtain is one layer of four: forecast, curtain, gathering boats, existing screens"));
            var cols = new VisualElement(); cols.AddToClassList("ev"); page.Add(cols);
            cols.Add(Column("SUPPORTED", "ev__col--good", new[]
            {
                "Bubble-curtain outflow from Bulson's relation: 0.45 m/s at 3 L/s/m holds approach currents up to ~0.27 m/s",
                "2025 approach current: mean 0.14 m/s, worst 5% 0.23 m/s (Copernicus + ERA5)",
                "Salmon farms: 77–80% fewer jellyfish with bubble curtains (industry reports)",
                "Forecast stress test: ~3% vs ~23% of arriving jellyfish reach the mouth before protection (Qandeel vs daylight camera alone)",
                "Detector: 91% recall on 116 held-out images",
            }));
            cols.Add(Column("MIXED OR AGAINST", "ev__col--warn", new[]
            {
                "UCC trials: mixed results; waves weaken curtains, not quantified, so not modelled",
                "Lo 1991: argues against bubbles alone",
                "Japanese plant intakes: no published data",
            }));
            cols.Add(Column("NOT YET KNOWN · THE SEA PILOT MEASURES", "ev__col--open", new[]
            {
                "Water depth at the mouth (up to ~9.5 m, not surveyed)",
                "Swarm depth: deep swarms can slip under the outflow layer (39–82% held at 2 m mean depth)",
                "Thickness of the outflow layer",
                "Wave effect on the curtain",
                "Sea pilot: one section, USD 0.28–0.72 M",
            }));
            return page;
        }

        // ---------- helpers
        static VisualElement Title(string t, string s)
        {
            var v = new VisualElement(); v.AddToClassList("info__head");
            var a = new Label(t); a.AddToClassList("info__title"); v.Add(a);
            var b = new Label(s); b.AddToClassList("note"); v.Add(b);
            return v;
        }
        static VisualElement Kpi(string k, string v)
        {
            var e = new VisualElement(); e.AddToClassList("kpi"); e.AddToClassList("kpi--panel");
            var a = new Label(k); a.AddToClassList("kpi__k"); e.Add(a);
            var b = new Label(v); b.AddToClassList("kpi__v"); e.Add(b);
            return e;
        }
        static Label Note(string t) { var l = new Label(t); l.AddToClassList("note"); return l; }
        static VisualElement Column(string head, string cls, string[] items)
        {
            var c = new VisualElement(); c.AddToClassList("ev__col"); c.AddToClassList(cls);
            var h = new Label(head); h.AddToClassList("ev__head"); c.Add(h);
            foreach (var it in items) { var l = new Label("• " + it); l.AddToClassList("ev__item"); c.Add(l); }
            return c;
        }
    }
}
