using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Tableau.Api.Models;
using Tableau.Api.Tests.Base;

namespace Tableau.Api.Tests
{
    [TestClass]
    public class A02_Authentication:TestBase
    {

        [TestMethod]
        public async Task A02_0_LoginToDefaultSiteAdminUser()
        {
            Context._defaultSession = await Context.Client.SignIn(Context.GetSetting<string>("User"),
                Context.GetSetting<string>("Password"));
            Assert.IsNotNull(Context._defaultSession, "Session is null");
        }

        [TestMethod]
        [PreRequisites(nameof(A02_0_LoginToDefaultSiteAdminUser))]
        public async Task A02_1_SwitchSite()
        {

            string oldSiteId = Context._defaultSession.SiteId;

            Context._defaultSession =
                await Context.Client.SwitchSite(Context._defaultSession, Context.GetSetting<string>("Site"));
            
            Assert.AreNotEqual(oldSiteId, Context._defaultSession.SiteId, $"Site IDs match ({oldSiteId})");
        }

        [TestMethod]
        public async Task A02_2_Logout()
        {
            await Context.Client.SignOut(Context._defaultSession);
            
        }
        [TestMethod]
        public async Task A02_3_LoginAdminUser()
        {
            Context.AdminSession = await Context.Client.SignIn(Context.GetSetting<string>("User"),
                Context.GetSetting<string>("Password"), Context.GetSetting<string>("Site"));
            Assert.IsNotNull(Context.AdminSession, "Session is null");
        }

        [TestMethod]
        public async Task A02_4_LoginNonAdminUser()
        {
            Context.NonAdminAdminSession = await Context.Client.SignIn(Context.GetSetting<string>("NonAdminUser"),
                Context.GetSetting<string>("NonAdminPassword"), Context.GetSetting<string>("Site"));
            Assert.IsNotNull(Context.NonAdminAdminSession, "Session is null");
        }
        [TestMethod]
        public async Task A02_5_ImpersonateNonAdminUser()
        {
            Context._nonAdminSessionImpersonate = await Context.Client.SignIn(Context.GetSetting<string>("User"),
                Context.GetSetting<string>("Password"), Context.GetSetting<string>("Site"), Context.GetSetting<string>("NonAdminUserId"));
            Assert.IsNotNull(Context._nonAdminSessionImpersonate, "Session is null");
            Assert.AreEqual(Context._nonAdminSessionImpersonate.UserId, Context.GetSetting<string>("NonAdminUserId"), "User was not impersonated");
        }
    }
}
