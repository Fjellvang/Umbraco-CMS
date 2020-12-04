﻿using System.Collections.Generic;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Moq;
using NUnit.Framework;
using Umbraco.Core;
using Umbraco.Core.Models;
using Umbraco.Core.Models.Entities;
using Umbraco.Core.Models.Membership;
using Umbraco.Core.Persistence.Querying;
using Umbraco.Core.Security;
using Umbraco.Core.Services;
using Umbraco.Tests.Common.Builders;
using Umbraco.Web.BackOffice.Authorization;

namespace Umbraco.Tests.UnitTests.Umbraco.Web.BackOffice.Authorization
{
    public class ContentPermissionsPublishBranchHandlerTests
    {
        private const int NodeId = 1000;
        private const int DescendentNodeId1 = 1001;
        private const int DescendentNodeId2 = 1002;

        [Test]
        public async Task User_With_Access_To_All_Descendent_Nodes_Is_Authorized()
        {
            var authHandlerContext = CreateAuthorizationHandlerContext();
            var mockUserService = CreateMockUserService(NodeId, new Dictionary<int, string[]>
            {
                { DescendentNodeId1, new string[] { "A" } },
                { DescendentNodeId2, new string[] { "A" } }
            });
            var sut = CreateHandler(mockUserService.Object, NodeId);

            await sut.HandleAsync(authHandlerContext);

            Assert.IsTrue(authHandlerContext.HasSucceeded);
            mockUserService.Verify(x => x.GetPermissionsForPath(It.IsAny<IUser>(), It.IsAny<string>()), Times.Exactly(2));
        }

        [Test]
        public async Task User_Without_Access_To_One_Descendent_Node_Is_Not_Authorized()
        {
            var authHandlerContext = CreateAuthorizationHandlerContext();
            var mockUserService = CreateMockUserService(NodeId, new Dictionary<int, string[]>
            {
                { DescendentNodeId1, new string[] { "A" } },
                { DescendentNodeId2, new string[] { "B" } }
            });
            var sut = CreateHandler(mockUserService.Object, NodeId);

            await sut.HandleAsync(authHandlerContext);

            Assert.IsFalse(authHandlerContext.HasSucceeded);
            mockUserService.Verify(x => x.GetPermissionsForPath(It.IsAny<IUser>(), It.IsAny<string>()), Times.Exactly(2));
        }

        [Test]
        public async Task User_Without_Access_To_First_Descendent_Node_Is_Not_Authorized_And_Checks_Exit_Early()
        {
            var authHandlerContext = CreateAuthorizationHandlerContext();
            var mockUserService = CreateMockUserService(NodeId, new Dictionary<int, string[]>
            {
                { DescendentNodeId1, new string[] { "B" } },
                { DescendentNodeId2, new string[] { "A" } }
            });
            var sut = CreateHandler(mockUserService.Object, NodeId);

            await sut.HandleAsync(authHandlerContext);

            Assert.IsFalse(authHandlerContext.HasSucceeded);
            mockUserService.Verify(x => x.GetPermissionsForPath(It.IsAny<IUser>(), It.IsAny<string>()), Times.Exactly(1));
        }

        private static AuthorizationHandlerContext CreateAuthorizationHandlerContext()
        {
            var requirement = new ContentPermissionsPublishBranchRequirement('A');
            var user = new ClaimsPrincipal(new ClaimsIdentity(new List<Claim>()));
            var resource = CreateContent(NodeId);
            return new AuthorizationHandlerContext(new List<IAuthorizationRequirement> { requirement }, user, resource);
        }

        private static Mock<IUserService> CreateMockUserService(int parentNodeId, Dictionary<int, string[]> descendendNodePermissionsForPath)
        {
            var mockUserService = new Mock<IUserService>();

            mockUserService
                .Setup(x => x.GetPermissionsForPath(It.IsAny<IUser>(), It.Is<string>(y => y == $"{Constants.System.Root},{parentNodeId},{DescendentNodeId1}")))
                .Returns(new EntityPermissionSet(parentNodeId, new EntityPermissionCollection(new List<EntityPermission> { new EntityPermission(1, parentNodeId, descendendNodePermissionsForPath[DescendentNodeId1]) })));
            mockUserService
                .Setup(x => x.GetPermissionsForPath(It.IsAny<IUser>(), It.Is<string>(y => y == $"{Constants.System.Root},{parentNodeId},{DescendentNodeId1},{DescendentNodeId2}")))
                .Returns(new EntityPermissionSet(parentNodeId, new EntityPermissionCollection(new List<EntityPermission> { new EntityPermission(1, parentNodeId, descendendNodePermissionsForPath[DescendentNodeId2]) })));

            return mockUserService;
        }

        private ContentPermissionsPublishBranchHandler CreateHandler(IUserService userService, int nodeId)
        {
            var mockEntityService = CreateMockEntityService();
            var contentPermissions = CreateContentPermissions(mockEntityService.Object, userService, nodeId);
            var mockBackOfficeSecurityAccessor = CreateMockBackOfficeSecurityAccessor();
            return new ContentPermissionsPublishBranchHandler(mockEntityService.Object, contentPermissions, mockBackOfficeSecurityAccessor.Object);
        }

        private static Mock<IEntityService> CreateMockEntityService()
        {
            long totalRecords;
            var mockEntityService = new Mock<IEntityService>();
            mockEntityService
                .Setup(x => x.GetPagedDescendants(It.Is<int>(y => y == NodeId), It.Is<UmbracoObjectTypes>(y => y == UmbracoObjectTypes.Document), It.IsAny<long>(), It.IsAny<int>(), out totalRecords, It.IsAny<IQuery<IUmbracoEntity>>(), It.IsAny<Ordering>()))
                .Returns(new List<IEntitySlim>
                {
                    new EntitySlim { Id = DescendentNodeId1, Path = $"-1,{NodeId},{DescendentNodeId1}" },
                    new EntitySlim { Id = DescendentNodeId2, Path = $"-1,{NodeId},{DescendentNodeId1},{DescendentNodeId2}" }
                });
            return mockEntityService;
        }

        private static ContentPermissions CreateContentPermissions(IEntityService entityService, IUserService userService, int nodeId)
        {
            var mockContentService = new Mock<IContentService>();
            mockContentService
                .Setup(x => x.GetById(It.Is<int>(y => y == nodeId)))
                .Returns(CreateContent(nodeId));

            return new ContentPermissions(userService, mockContentService.Object, entityService);
        }

        private static IContent CreateContent(int nodeId)
        {
            var contentType = ContentTypeBuilder.CreateBasicContentType();
            return ContentBuilder.CreateBasicContent(contentType, nodeId);
        }

        private static Mock<IBackOfficeSecurityAccessor> CreateMockBackOfficeSecurityAccessor()
        {
            var user = CreateUser();
            var mockBackOfficeSecurity = new Mock<IBackOfficeSecurity>();
            mockBackOfficeSecurity.SetupGet(x => x.CurrentUser).Returns(user);
            var mockBackOfficeSecurityAccessor = new Mock<IBackOfficeSecurityAccessor>();
            mockBackOfficeSecurityAccessor.Setup(x => x.BackOfficeSecurity).Returns(mockBackOfficeSecurity.Object);
            return mockBackOfficeSecurityAccessor;
        }

        private static User CreateUser()
        {
            return new UserBuilder()
                .Build();
        }
    }
}
