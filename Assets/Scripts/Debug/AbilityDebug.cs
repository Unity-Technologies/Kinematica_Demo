using System;
using System.Collections.Generic;
using Unity.SnapshotDebugger;

public struct AbilityRecord : IFrameRecord
{
    public Type abilityType;
}

public struct AbilityState
{
    public Type abilityType;
    public float startTime;
    public float endTime;
}

public class AbilityFrameAggregate : IFrameAggregate
{
    public IEnumerable<AbilityState> States
    {
        get
        {
            for(int i = 0; i < m_AbilityStates.Count; ++i)
            {
                yield return m_AbilityStates[i];
            }
        }
    }

    public bool IsEmpty => m_AbilityStates.Count == 0;

    public void AddRecords(List<IFrameRecord> records, float frameStartTime, float frameEndTime)
    {
        foreach(IFrameRecord record in records)
        {
            AbilityRecord abilityRecord = (AbilityRecord)record;

            AbilityState state;

            if (m_AbilityStates.Count > 0 && m_AbilityStates.Last.abilityType == abilityRecord.abilityType)
            {
                state = m_AbilityStates.Last;
                m_AbilityStates.PopBack();
                state.endTime = frameEndTime;
            }
            else
            {
                state = new AbilityState()
                {
                    abilityType = abilityRecord.abilityType,
                    startTime = frameStartTime,
                    endTime = frameEndTime
                };
            }

            m_AbilityStates.PushBack(state);
        }
    }

    public void PruneFramesBeforeTimestamp(float startTimeInSeconds)
    {
        while(m_AbilityStates.Count > 0 && m_AbilityStates[0].endTime <= startTimeInSeconds)
        {
            m_AbilityStates.PopFront();
        }

        if (m_AbilityStates.Count > 0)
        {
            AbilityState state = m_AbilityStates[0];
            if (state.startTime < startTimeInSeconds)
            {
                state.startTime = startTimeInSeconds;
                m_AbilityStates[0] = state;
            }
        }
    }

    public void PruneFramesStartingAfterTimestamp(float endTimeInSeconds)
    {
        while (m_AbilityStates.Count > 0 && m_AbilityStates.Last.startTime >= endTimeInSeconds)
        {
            m_AbilityStates.PopBack();
        }

        if (m_AbilityStates.Count > 0)
        {
            AbilityState state = m_AbilityStates.Last;
            if (state.endTime > endTimeInSeconds)
            {
                state.endTime = endTimeInSeconds;
                m_AbilityStates[m_AbilityStates.Count - 1] = state;
            }
        }
    }


    CircularList<AbilityState> m_AbilityStates = new CircularList<AbilityState>();
}