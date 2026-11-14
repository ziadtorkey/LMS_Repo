using Lms.Shared.Timetable;

namespace Lms.Client.Components.Timetable;

/// <summary>A lesson dropped on another cell (by keyboard, menu or drag): the parent saves it.</summary>
public sealed record SlotMove(SlotDto Slot, DayOfWeek Day, Guid PeriodId);
