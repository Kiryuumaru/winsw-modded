using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace WinSW.Core.Extensions;

internal static class SafeExtensions
{
    public static void RunAndForget(Action action)
    {
        try
        {
            action();
        }
        catch
        {
        }
    }
}
