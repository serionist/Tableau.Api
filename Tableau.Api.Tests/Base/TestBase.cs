using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Tableau.Api.Tests.Base;

namespace Tableau.Api.Tests
{
    public abstract class TestBase
    {
        public TestContext TestContext { get; set; }

        protected static readonly Context Context = new Context();



        protected Stream GetResource(string resourceName)
        {
            return File.OpenRead(Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location),
                "Resources", resourceName));
        }

        private static List<string> Ran = new List<string>();

        [TestInitialize]
        public void TestInitialize()
        {
           _runPrereq(this.GetType(), TestContext.TestName);
        }

        private void _runPrereq(Type srcType, string methodName)
        {
            try
            {
                var method = srcType.GetMethod(methodName);
                if (method == null)
                    throw new ArgumentException("methodName is invalid");
                Ran.Add(methodName);
                if (!(method.GetCustomAttribute(typeof(PreRequisitesAttribute)) is PreRequisitesAttribute prerequisiteAttribute))
                    return;
                var prerequisiteFunctions = prerequisiteAttribute.MethodNames;
                foreach (var func in prerequisiteFunctions)
                {
                    if (Ran.Exists(e => e == func))
                        continue;
                    var targetMethod = _findMethod(func);
                    if (targetMethod == null)
                        throw new ArgumentException($"targetMethod: {func} is invalid");
                    if (targetMethod.GetParameters().Any())
                        throw new ArgumentException($"targetMethod: {func} has input parameters");
                    if (targetMethod.ReturnType != null && targetMethod.ReturnType != typeof(Task))
                        throw new ArgumentException($"targetMethod: {func} does not return void or Task");
                    _runPrereq(targetMethod.DeclaringType, targetMethod.Name);
                    try
                    {
                        if (targetMethod.ReturnType == null)
                            targetMethod.Invoke(Activator.CreateInstance(targetMethod.DeclaringType), null);
                        else if (targetMethod.ReturnType == typeof(Task))
                            ((Task)targetMethod.Invoke(Activator.CreateInstance(targetMethod.DeclaringType), null)).Wait();
                        Ran.Add(func);
                    }
                    catch (Exception e)
                    {
                        throw new Exception($"Prerequisite: {func} failed", (e as AggregateException)?.InnerException ?? e);
                    }


                }
                InitializeTest(TestContext.TestName).Wait();
            }
            catch (AggregateException ex)
            {
                throw ex.InnerException;
            }
        }

        private MethodInfo _findMethod(string name)
        {
            return typeof(TestBase).Assembly.GetTypes()
                .Where(e => e.IsSubclassOf(typeof(TestBase)) && !e.IsAbstract).First(t => t.GetMethod(name) != null)
                .GetMethod(name);
        }

        private static async Task InitializeTest([CallerMemberName]string methodName = null)
        {
           
           
        }


    }
}
