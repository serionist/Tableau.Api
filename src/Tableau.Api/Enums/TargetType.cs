namespace Tableau.Api.Enums;

public enum TargetType
{
    View,
    Workbook,
    // Tableau Server 2025.1 sends FLOW for a subscription to a flow. Last, so no member's value changes.
    Flow
}
