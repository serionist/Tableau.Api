using System;
using System.Collections.Generic;
using System.Text;

namespace Tableau.Api.VizPortal.Models;

// Content - Workbooks and Datasources
public class ContentUser
{
    public string DisplayName { get; set; }
    public string DomainName { get; set; }
    public bool? HasServerDisplay { get; set; }
    public string Id { get; set; }
    public bool? ReadOnly { get; set; }
    public string UserName { get; set; }
}

public class ContentProjectDs
{
    public string Id { get; set; }
    public string Name { get; set; }
    public Content Contents { get; set; }
    public class Content
    {
        public int[] DATASOURCE { get; set; }
    }
}

public class ContentProjectWb
{
    public string Id { get; set; }
    public string Name { get; set; }
    public Content Contents { get; set; }
    public class Content
    {
        public int[] WORKBOOK { get; set; }
    }
}

public class MoveContent
{
    public string ContainerId { get; set; }
    public string ContainerType { get; set; } = "PROJECT";
    public Content[] ContentItems { get; set; }

    public class Content
    {
        public string ContentId { get; set; }
        public string ContentType { get; set; }
        public string MoveType { get; set; } = null;
    }
}

public class RenameContent
{
    public string Id { get; set; }
    public string Name { get; set; }
}

public class ChangeContentOwner
{
    public string[] Ids { get; set; }
    public int OwnerId { get; set; }
}

public class DeleteContent
{
    public string[] Ids { get; set; }
}
