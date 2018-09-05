using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Tableau.Api.Models
{
    public class tsResponse<T> :tsResponse where T:class
    {
        public T Item1 => base.Items.FirstOrDefault(e => e is T) as T;
    }
}
