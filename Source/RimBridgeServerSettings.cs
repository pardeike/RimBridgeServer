using Verse;
using UnityEngine;

namespace RimBridgeServer;

internal sealed class RimBridgeServerSettings : ModSettings
{
    public bool SemanticHarnessSmokeToggle;

    public int SemanticHarnessSmokeValue = 1;

    public override void ExposeData()
    {
        Scribe_Values.Look(ref SemanticHarnessSmokeToggle, "semanticHarnessSmokeToggle", false);
        Scribe_Values.Look(ref SemanticHarnessSmokeValue, "semanticHarnessSmokeValue", 1);
    }
}

internal static class RimBridgeServerSettingsDrawer
{
    private static string _semanticHarnessSmokeBuffer = "1";

    public static void Draw(Rect inRect, RimBridgeServerSettings settings)
    {
        var listing = new Listing_Standard();
        listing.Begin(inRect);
        listing.Label("Reserved settings used by the live smoke harness to validate semantic mod-settings discovery and persistence.");
        listing.Gap();
        listing.CheckboxLabeled(
            "Semantic harness smoke toggle",
            ref settings.SemanticHarnessSmokeToggle,
            "This boolean exists so automated tests can round-trip a known persistent setting safely.");
        listing.Gap();
        listing.TextFieldNumericLabeled("Semantic harness smoke value", ref settings.SemanticHarnessSmokeValue, ref _semanticHarnessSmokeBuffer, 0, 9999);
        listing.End();
    }
}
