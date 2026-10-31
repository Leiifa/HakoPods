using AirPods.Core.State;

namespace AirPods.Core.Presentation;

/// <summary>The five glyphs the design defines.</summary>
public enum TrayGlyph
{
    Disconnected,
    Unknown,
    Low,
    Charging,
    Full,
}

/// <summary>
/// Turns a <see cref="DeviceState"/> into exactly one glyph, following `docs/design/ICONS.md`:
/// disconnected &gt; unknown &gt; low &gt; charging &gt; full.
///
/// The precedence is the contract, not a detail: a pair that is present but reading unknown must
/// never keep showing a stale "full" glyph, and a low bud must outrank a charging case.
///
/// Lives in Core rather than the app so it can be tested without a desktop session.
/// </summary>
public static class TrayGlyphMapper
{
    public const int LowBatteryThreshold = 10;

    public static TrayGlyph From(DeviceState state)
    {
        ArgumentNullException.ThrowIfNull(state);

        if (!state.IsPresent)
        {
            return TrayGlyph.Disconnected;
        }

        if (!state.HasAnyBattery)
        {
            return TrayGlyph.Unknown;
        }

        if (LowestKnownPercent(state) is <= LowBatteryThreshold)
        {
            return TrayGlyph.Low;
        }

        return state.LeftCharging || state.RightCharging || state.CaseCharging
            ? TrayGlyph.Charging
            : TrayGlyph.Full;
    }

    /// <summary>Tooltip body: "AirPods Pro · L 80 % · R — · Case 60 %". Unknown is an em dash.</summary>
    public static string Describe(DeviceState state)
    {
        ArgumentNullException.ThrowIfNull(state);

        if (!state.IsPresent)
        {
            return state.ModelName is null ? "No AirPods nearby" : $"{state.ModelName} · not nearby";
        }

        var parts = new List<string>(3);

        if (state.LeftBatteryPercent is not null)
        {
            parts.Add($"L {state.LeftBatteryPercent} %");
        }

        if (state.RightBatteryPercent is not null)
        {
            parts.Add($"R {state.RightBatteryPercent} %");
        }

        if (state.CaseBatteryPercent is not null)
        {
            parts.Add($"Case {state.CaseBatteryPercent} %");
        }

        if (parts.Count == 0)
        {
            parts.Add("battery unknown");
        }

        return $"{state.ModelName ?? "AirPods"} · {string.Join(" · ", parts)}";
    }

    public static int? LowestKnownPercent(DeviceState state)
    {
        var values = new[] { state.LeftBatteryPercent, state.RightBatteryPercent, state.CaseBatteryPercent }
            .Where(v => v is not null)
            .Select(v => v!.Value)
            .ToArray();

        return values.Length == 0 ? null : values.Min();
    }
}
