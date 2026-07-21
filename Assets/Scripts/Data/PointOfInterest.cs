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

    // What studying this site COSTS in research points, and what it PAYS BACK on completion. Both vary
    // enormously by what it is: a rich ore seam is cheap to confirm and pays little, while precursor
    // ruins are a serious investment that can hand back an ancient schematic — the only way into the
    // Ancients tech tree.
    public int researchPointCost = 20;
    public int researchReward = 25;
    public bool yieldsSchematic = false;   // precursor ruins can recover a schematic

    // Has a research ship charted this site closely enough to work out what studying it would involve?
    //
    // A DEEP SURVEY sets this — and that is all a deep survey does to a site. The ship does not dig; it
    // finds, identifies, and works out the price. What the survey produces is an OPPORTUNITY: a job you
    // can commission, that costs resources and research points, runs on a timer with a progress bar, and
    // pays out in ore or technology when it lands. The deep survey used to resolve every site on the
    // spot for free, which meant the excavation system existed but could never be reached — the button
    // to start a dig was removed by the very act that revealed the dig.
    public bool surveyed = false;

    // Ruins are researchable until studied; settlements are context, not a project.
    public bool IsResearchable => surveyed &&
                                  ((type == POIType.Mystery && !explored)
                                   || (type == POIType.AncientRuins && !explored)
                                   || (type == POIType.SpecialResource && relatedOre != OreType.None && !ResearchManager.IsResearched(relatedOre)));

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
