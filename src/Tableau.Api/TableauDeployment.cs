namespace Tableau.Api;

/// <summary>Which Tableau a client talks to, which decides the calls it may make.</summary>
public enum TableauDeployment
{
    /// <summary>
    /// Tableau Cloud when <see cref="TableauClientOptions.ServerUrl"/>'s host is <c>online.tableau.com</c> or one of its
    /// pods, such as <c>prod-useast-a.online.tableau.com</c>; Tableau Server otherwise.
    /// </summary>
    Auto,

    /// <summary>Tableau Server, run on the customer's own machines.</summary>
    Server,

    /// <summary>Tableau Cloud, Tableau's hosted service.</summary>
    Cloud,
}
