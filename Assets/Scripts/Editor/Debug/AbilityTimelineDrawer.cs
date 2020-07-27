using System;
using UnityEngine;

using Unity.SnapshotDebugger;
using Unity.SnapshotDebugger.Editor;

public class AbilityTimelineDrawer : ITimelineDebugDrawer
{
    public Type AggregateType => typeof(AbilityFrameAggregate);

    public float GetDrawHeight(IFrameAggregate aggregate)
    {
        return kTimelineHeight;
    }

    public void Draw(FrameDebugProviderInfo providerInfo, IFrameAggregate aggregate,TimelineWidget.DrawInfo drawInfo)
    {
        AbilityFrameAggregate abilityAggregate = (AbilityFrameAggregate)aggregate;

        foreach(AbilityState state in abilityAggregate.States)
        {
            float startPosition = drawInfo.GetPixelPosition(state.startTime);
            float endPosition = drawInfo.GetPixelPosition(state.endTime);
            Rect abilityRect = new Rect(startPosition, drawInfo.timeline.drawRect.y, endPosition - startPosition, kAbilityRectHeight);

            TimelineWidget.DrawRectangleWithDetour(abilityRect, kAbilityWidgetBackgroundColor, kAbilityWidgetDetourColor);
            TimelineWidget.DrawLabelInsideRectangle(abilityRect, state.abilityType.FullName, kAbilityWidgetTextColor);
        }
    }

    public void OnPostDraw(){}

    static readonly float kAbilityRectHeight = 25.0f;
    static readonly float kTimelineHeight = 35.0f;

    static readonly Color kAbilityWidgetBackgroundColor = new Color(0.2f, 0.4f, 0.2f, 1.0f);
    static readonly Color kAbilityWidgetDetourColor = new Color(0.5f, 0.8f, 0.5f, 1.0f);
    static readonly Color kAbilityWidgetTextColor = new Color(0.5f, 0.8f, 0.5f, 1.0f);
}

