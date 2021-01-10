using System;
using System.Collections.Generic;
using System.Text;

namespace Tableau.Api.Tests.Base
{
    public class PreRequisitesAttribute: Attribute
    {
        public PreRequisitesAttribute(params string[] methodNames)
        {
            MethodNames = methodNames;
        }

        public string[] MethodNames { get; }
    }
}
