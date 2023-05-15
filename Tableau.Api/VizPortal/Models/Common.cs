using System;
using System.Collections.Generic;
using System.Text;

namespace Tableau.Api.VizPortal.Models
{
    public class QueryModel
    {
        public FilterModel Filter { get; set; }
        public OrderModel[] Order { get; set; }
        public PageModel Page { get; set; }
        public class FilterModel
        {
            public Clause[] Clauses { get; set; }
            public string Operator { get; set; }
            public class Clause
            {
                public string Field { get; set; }
                public string Operator { get; set; }
                public string Value { get; set; }
            }

        }

        public class OrderModel
        {
            public string Field { get; set; }
            public bool Ascending { get; set; }
        }

        public class PageModel
        {
            public long StartIndex { get; set; }
            public long MaxItems { get; set; }
        }

    }

    public class QueryResponseModel
    {
        public bool MoreItems { get; set; }
        public long TotalCount { get; set; }
    }

    public class Empty
    {

    }

   
}
