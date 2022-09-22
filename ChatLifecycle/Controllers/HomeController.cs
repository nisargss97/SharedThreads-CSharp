// <copyright file="HomeController.cs" company="Microsoft">
// Copyright (c) Microsoft. All Rights Reserved.
// </copyright>

namespace ChatLifecycle.Controllers
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.Linq;
    using System.Net.Http;
    using System.Threading.Tasks;
    using AdaptiveCards.Templating;
    using ChatLifecycle.Helper;
    using ChatLifecycle.Models;
    using Microsoft.AspNetCore.Authorization;
    using Microsoft.AspNetCore.Http;
    using Microsoft.AspNetCore.Mvc;
    using Microsoft.Extensions.Configuration;
    using Microsoft.Graph;
    using Newtonsoft.Json;

    public class HomeController : Controller
    {
        private readonly IConfiguration _configuration;
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly IHttpContextAccessor _httpContextAccessor;
     
        /// <summary>
        /// Initializes a new instance of the <see cref="HomeController"/> class.
        /// </summary>
        /// <param name="configuration">IConfiguration instance.</param>
        /// <param name="httpClientFactory">IHttpClientFactory instance.</param>
        /// <param name="httpContextAccessor">IHttpContextAccessor instance.</param>
        public HomeController(
            IConfiguration configuration,
            IHttpClientFactory httpClientFactory,
            IHttpContextAccessor httpContextAccessor)
        {
            _configuration = configuration;
            _httpClientFactory = httpClientFactory;
            _httpContextAccessor = httpContextAccessor;
        }

        public IActionResult Index()
        {
            return View();
        }

        public IActionResult Configure()
        {

            return View();
        }
        /// <summary>
        /// Retrieve team members along with profile pictures
        /// </summary>
        /// <returns>Returns Team members details</returns>
        [Authorize]
        [HttpGet("GetUserAccessToken")]
        public async Task<ActionResult<string>> GetUserAccessToken()
        {
            try
            {
                 var accessToken = await SSOAuthHelper.GetAccessTokenOnBehalfUserAsync(_configuration, _httpClientFactory, _httpContextAccessor);
                return accessToken;
            }
            catch (Exception e)
            {
                return null;
            }
        }
        
       
        [Route("GetAdaptiveCard")]
        public string GetAdaptiveCard(string token)
        {
            var graphClient = GraphClient.GetGraphClient(token);
            var users = graphClient.Users
            .Request()
            .GetAsync().Result;

            string adaptiveCardJson = System.IO.File.ReadAllText("Resources/Cards/AdaptiveCard.json");


            // Create a Template instance from the template payload
            AdaptiveCardTemplate template = new AdaptiveCardTemplate(adaptiveCardJson);

            // You can use any serializable object as your data
            var payloadData = new
            {
                user1Title = users.CurrentPage[0].DisplayName,
                user1Value = users.CurrentPage[0].Id,
                user2Title = users.CurrentPage[1].DisplayName,
                user2Value = users.CurrentPage[1].Id,
                user3Title = users.CurrentPage[2].DisplayName,
                user3Value = users.CurrentPage[2].Id,
                user4Title = users.CurrentPage[3].DisplayName,
                user4Value = users.CurrentPage[3].Id,
                user5Title = users.CurrentPage[4].DisplayName,
                user5Value = users.CurrentPage[4].Id,
                user6Title = users.CurrentPage[5].DisplayName,
                user6Value = users.CurrentPage[5].Id,
            };

            //"Expand" the template -this generates the final Adaptive Card payload
            string cardJson = template.Expand(payloadData);
            return cardJson;
        }

        [Route("CreateNewTeam")]
        public bool CreateNewTeam(string token,string resultJson, string userID, string title)
        {
            string[] members = { };
            if (resultJson.Contains(','))
            {
                members = resultJson.Split(',');
            }
            else
            {
                members = new[] { resultJson };
            }

            var graphClient = GraphClient.GetGraphClient(token);
            CreateGroupChat(graphClient, members, userID, title);
            return true;
        }

        [Route("GetChats")]
        public async Task<string> GetChats(string token, string otherUserId, string userID, string title)
        {
            var graphClient = GraphClient.GetGraphClient(token);
            //chatType eq 'meeting' or chatType eq 'group'
            IGraphServiceChatsCollectionPage chatsTask =  await graphClient.Chats.Request().Filter("chatType eq 'meeting' or chatType eq 'group'").Expand("members").GetAsync();

            List<string> chats = new List<string>();
            foreach(var chat in chatsTask.CurrentPage)
            {
                var found = chat.Members.CurrentPage.Select(member => (AadUserConversationMember) member).Any(member => member.UserId == otherUserId);
                if (found) chats.Add(chat.Topic);
            }    

            return JsonConvert.SerializeObject(chats);
        }

        public async void CreateGroupChat(GraphServiceClient graphClient, string[] members, string userID, string title)
        {
            var chat = new Chat
            {
                ChatType = ChatType.Group,
                Topic = title,
                Members = new ChatMembersCollectionPage()
                {
                      new AadUserConversationMember
                      {
                           Roles = new List<String>()
                           {
                             "owner"
                           },
                      AdditionalData = new Dictionary<string, object>()
                      {
                        {"user@odata.bind", "https://graph.microsoft.com/v1.0/users('"+members[0]+"')"}
                      }
                      },
                    new AadUserConversationMember
                    {
                       Roles = new List<String>()
                       {
                         "owner"
                       },
                    AdditionalData = new Dictionary<string, object>()
                    {
                         {"user@odata.bind", "https://graph.microsoft.com/v1.0/users('"+userID+"')"}
                    }
                    }
                }
            };

            var response = await graphClient.Chats
                 .Request()
                 .AddAsync(chat);

            if (members.Length == 2)
            {
                AddMemberWithHistory(graphClient, response, members);
                DeleteMember(graphClient,response);
            }

            else if (members.Length == 3)
            {
                AddMemberWithHistory(graphClient, response, members);
                AddMemberWithoutHistory(graphClient, response, members);
                DeleteMember(graphClient, response);
            }

            else if (members.Length >= 4)
            {
                AddMemberWithHistory(graphClient, response, members);
                AddMemberWithoutHistory(graphClient, response, members);
                AddMemberWithNoOfDays(graphClient, response, members);
                DeleteMember(graphClient, response);
            }

            //Adding Polly app to chat

            var teamsAppInstallation = new TeamsAppInstallation
            {
                AdditionalData = new Dictionary<string, object>()
                {
                   {"teamsApp@odata.bind", "https://graph.microsoft.com/v1.0/appCatalogs/teamsApps/1542629c-01b3-4a6d-8f76-1938b779e48d"}
                }
            };

            await graphClient.Chats[response.Id].InstalledApps
                .Request()
                .AddAsync(teamsAppInstallation);


            //Adding Polly as tab to chat
            var teamsTab = new TeamsTab
            {
                DisplayName = "Associate Insights",
                Configuration = new TeamsTabConfiguration
                {
                    EntityId = "pollyapp",
                    ContentUrl = "https://teams.polly.ai/msteams/content/meeting/tab?theme={theme}",
                    WebsiteUrl = null,
                    RemoveUrl = "https://teams.polly.ai/msteams/content/tabdelete?theme={theme}"
                },
                AdditionalData = new Dictionary<string, object>()
                {
                  {"teamsApp@odata.bind", "https://graph.microsoft.com/v1.0/appCatalogs/teamsApps/1542629c-01b3-4a6d-8f76-1938b779e48d"}
                }
            };

            await graphClient.Chats[response.Id].Tabs
                .Request()
                .AddAsync(teamsTab);
        }


        public async void AddMemberWithHistory(GraphServiceClient graphClient, Chat response, string[] members)
        {
            var conversationMember = new AadUserConversationMember
            {
                VisibleHistoryStartDateTime = DateTimeOffset.Parse("0001-01-01T00:00:00Z"),
                Roles = new List<string>() { "owner" },
                AdditionalData = new Dictionary<string, object>()
                {
                   {"user@odata.bind", "https://graph.microsoft.com/v1.0/users/"+members[1]}
                }
            };

            await graphClient.Chats[response.Id].Members
                .Request()
                .AddAsync(conversationMember);
        }

        public async void AddMemberWithoutHistory(GraphServiceClient graphClient, Chat response, string[] members)
        {
            var conversationMember = new AadUserConversationMember
            {
                Roles = new List<string>() { "owner" },
                AdditionalData = new Dictionary<string, object>()
                {
                  {"user@odata.bind", "https://graph.microsoft.com/v1.0/users/"+members[2]}
                }
            };

            await graphClient.Chats[response.Id].Members
                .Request()
                .AddAsync(conversationMember);
        }

        public async void AddMemberWithNoOfDays(GraphServiceClient graphClient, Chat response, string[] members)
        {
            if (members.Length == 4)
            {
                var conversationMember = new AadUserConversationMember
                {
                    VisibleHistoryStartDateTime = DateTimeOffset.Parse("2021-05-20T00:51:43.255Z"),
                    Roles = new List<string>() { "owner" },
                    AdditionalData = new Dictionary<string, object>()
                    {
                        {"user@odata.bind", "https://graph.microsoft.com/v1.0/users/"+members[3]}
                    }
                };

                await graphClient.Chats[response.Id].Members
                    .Request()
                    .AddAsync(conversationMember);
            }
            else
            {
                foreach (var member in members)
                {
                    var conversationMember = new AadUserConversationMember
                    {
                        VisibleHistoryStartDateTime = DateTimeOffset.Parse("2021-05-20T00:51:43.255Z"),
                        Roles = new List<string>() { "owner" },
                        AdditionalData = new Dictionary<string, object>()
                        {
                            {"user@odata.bind", "https://graph.microsoft.com/v1.0/users/"+member}
                        }
                    };

                    await graphClient.Chats[response.Id].Members
                        .Request()
                        .AddAsync(conversationMember);
                }
            }
        }

        public async void DeleteMember(GraphServiceClient graphClient, Chat response)
        {
            var chat = await graphClient.Chats[response.Id]
                .Request()
                .Expand("members")
                .GetAsync();
            var convMemID = chat.Members.CurrentPage[0].Id;

            await graphClient.Chats[response.Id].Members[convMemID]
                .Request()
                .DeleteAsync();
        }


        [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
        public IActionResult Error()
        {

            return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
        }

    }
}
