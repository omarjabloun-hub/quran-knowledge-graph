using System;
using System.Linq;
using QuranKG.FrontEnd.Api;
using Tesserae;
using UID;
using static Tesserae.UI;
using static Transpose.Core.dom;

namespace QuranKG.FrontEnd.Components
{
    /// <summary>
    /// The two charts of the look, drawn in HTML rather than SVG so an Arabic label shapes right to
    /// left and every bar can open what it counted: ranked bars (a root, a verse, a surah, each with
    /// its number) and columns (one per surah or verse, coloured by what the caller says).
    /// </summary>
    internal static class Charts
    {
        /// <summary>
        /// Ranked bars: the label, a bar proportional to the largest value, the number. A row opens
        /// its node (Uid) unless <paramref name="open"/> says otherwise; its Detail is the tooltip.
        /// </summary>
        internal static IComponent Bars(QK.Counted[] rows, string color, bool arabicLabels = true, Func<QK.Counted, double> value = null,
                                        Func<QK.Counted, string> valueText = null, Action<QK.Counted> open = null, int labelWidth = 0)
        {
            Look.EnsureStyles();
            if (rows is null || rows.Length == 0) return Raw(Look.El("p", "qkg-note", "Nothing to show."));

            var custom = open is object;
            value     = value     ?? (r => r.Count);
            valueText = valueText ?? (r => r.Count.ToString("N0"));
            open      = open      ?? (r => Lists.Open(r.Uid));

            var max  = Math.Max(1e-9, rows.Max(r => value(r)));
            var list = Look.El("div", "qkg-bars");
            if (labelWidth > 0) list.style.setProperty("--qkg-label", labelWidth + "px");
            else if (!arabicLabels) list.style.setProperty("--qkg-label", "150px");

            foreach (var row in rows)
            {
                var captured = row;
                var line = Look.El("div", "qkg-bar-row");
                line.appendChild(arabicLabels ? Look.Ar(row.Label, "qkg-arw qkg-bar-label") : Look.El("span", "qkg-bar-label", row.Label));
                line.appendChild(Look.Track(Math.Max(0, value(row)) / max, color));
                line.appendChild(Look.El("span", "qkg-bar-value", valueText(row)));
                if (!string.IsNullOrEmpty(row.Detail)) line.title = row.Detail;
                if (custom || UID128.IsNotNull(row.Uid)) Look.OnClick(line, () => open(captured));
                list.appendChild(line);
            }
            return Raw(list).WS();
        }

        /// <summary>
        /// Columns, one per value, on a scale from 0 to <paramref name="max"/> (the largest value when 0),
        /// with three gridlines and the axis labels given. A column with a click opens what it stands for.
        /// </summary>
        internal static IComponent Columns(double[] values, Func<int, string> color, Func<int, string> title, Action<int> open = null,
                                           int height = 220, double max = 0, string[] xLabels = null, Func<double, string> yFormat = null, string label = null)
        {
            Look.EnsureStyles();
            values  = values ?? new double[0];
            max     = max > 0 ? max : Math.Max(1e-9, values.DefaultIfEmpty(0).Max());
            yFormat = yFormat ?? (v => v.ToString("0.#"));

            var chart = Look.El("div", "qkg-columns-chart");

            var y = Look.El("div", "qkg-yaxis");
            y.style.height = height + "px";
            y.appendChild(Look.El("span", "", yFormat(max)));
            y.appendChild(Look.El("span", "", yFormat(max / 2)));
            y.appendChild(Look.El("span", "", "0"));
            chart.appendChild(y);

            var cols = Look.El("div", "qkg-cols");
            cols.style.height = height + "px";
            cols.style.gap    = values.Length > 200 ? "0px" : values.Length > 120 ? "1px" : "2px";
            var grid = Look.El("div", "qkg-cols-grid");
            grid.appendChild(Look.El("span", ""));
            grid.appendChild(Look.El("span", ""));
            grid.appendChild(Look.El("span", "", ""));
            cols.appendChild(grid);

            for (var i = 0; i < values.Length; i++)
            {
                var index = i;
                var col = Look.El("i", "");
                col.style.height     = (100 * Math.Max(0, Math.Min(1, values[i] / max))).ToString("0.##") + "%";
                col.style.background = color(i);
                col.title            = title(i);
                if (open is object) { col.classList.add("qkg-click"); col.onclick = _ => open(index); }
                cols.appendChild(col);
            }
            chart.appendChild(cols);

            if (xLabels is object && xLabels.Length > 0)
            {
                chart.appendChild(Look.El("span", ""));
                var x = Look.El("div", "qkg-xaxis");
                foreach (var tick in xLabels) x.appendChild(Look.El("span", "", tick));
                chart.appendChild(x);
            }

            chart.setAttribute("role", "img");
            if (label is object) chart.setAttribute("aria-label", label);
            return Raw(chart).WS();
        }
    }
}
