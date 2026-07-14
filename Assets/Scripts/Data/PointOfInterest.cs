// A notable location on a planet surface, shown in the detailed map view.
public enum POIType
{
    AncientRuins,      // remnants of a long-dead civilization
    Settlement,        // a current, living civilization
    SpecialResource,   // an exceptional ore / material deposit
    Mystery            // a "?" that requires further exploration to reveal
}

public class PointOfInterest
{
    public POIType type;
    public float u;                 // normalized surface position (0..1)
    public float v;
    public string title;
    public string description;
    public bool explored = false;   // Mysteries start unexplored

    public OreType relatedOre = OreType.None;   // for SpecialResource
    public string revealTitle;      // shown after a Mystery is explored
    public string revealText;       // what the exploration uncovered

    public string kind;             // flavour label: "Wreckage", "Cavern", "Unidentified Ore"...
    public float researchDuration = 12f;  // seconds to research (varies per opportunity)
    public string reportText;       // full report shown when research completes

    public bool IsResearchable => (type == POIType.Mystery && !explored)
                                  || (type == POIType.SpecialResource && relatedOre != OreType.None && !ResearchManager.IsResearched(relatedOre));

    public string HoverText()
    {
        switch (type)
        {
            case POIType.Mystery:
                return explored
                    ? $"<b>{revealTitle}</b>\n{revealText}"
                    : "<b>? Unknown Anomaly</b>\nFurther exploration required to reveal what lies here.";
            case POIType.Settlement:
                return $"<b>{title}</b>\n{description}";
            case POIType.AncientRuins:
                return $"<b>{title}</b>\n{description}";
            case POIType.SpecialResource:
                string ore = relatedOre != OreType.None ? OreDatabase.Get(relatedOre).displayName : "Unknown";
                return $"<b>{title}</b>\nRich {ore} deposit.\n{description}";
            default:
                return title;
        }
    }
}
