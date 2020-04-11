using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Web;
using System.Web.Mvc;
using Microsoft.AspNet.Identity;
using Microsoft.Owin.Security;
using Calendar.ASP.NET.MVC5.Models;
using Google.Apis.Auth.OAuth2;
using Google.Apis.Auth.OAuth2.Flows;
using Google.Apis.Auth.OAuth2.Responses;
using Google.Apis.Calendar.v3;
using Google.Apis.Services;
using Google.Apis.Util.Store;
using Google.Apis.Tasks.v1;
using Google.Apis.Tasks.v1.Data;
using Task = System.Threading.Tasks.Task;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json;
using System.Threading;

namespace Calendar.ASP.NET.MVC5.Controllers
{
    [Authorize]
    public class CalendarController : Controller
    {
        private readonly IDataStore dataStore = new FileDataStore(GoogleWebAuthorizationBroker.Folder);

        private async Task<UserCredential> GetCredentialForApiAsync()
        {
            var initializer = new GoogleAuthorizationCodeFlow.Initializer
            {
                ClientSecrets = new ClientSecrets
                {
                    ClientId = MyClientSecrets.ClientId,
                    ClientSecret = MyClientSecrets.ClientSecret,
                },
                Scopes = MyRequestedScopes.Scopes,
            };
            var flow = new GoogleAuthorizationCodeFlow(initializer);

            var identity = await HttpContext.GetOwinContext().Authentication.GetExternalIdentityAsync(
                DefaultAuthenticationTypes.ApplicationCookie);
            var userId = identity.FindFirstValue(MyClaimTypes.GoogleUserId);

            var token = await dataStore.GetAsync<TokenResponse>(userId); ;
            return new UserCredential(flow, userId, token);
        }

        // GET: /Calendar/UpcomingEvents
        public async Task<ActionResult> UpcomingEvents()
        {
            const int MaxEventsPerCalendar = 20;
            const int MaxEventsOverall = 50;

            var model = new UpcomingEventsViewModel();

            var credential = await GetCredentialForApiAsync();

            var initializer = new BaseClientService.Initializer()
            {
                HttpClientInitializer = credential,
                ApplicationName = "MetaHealth",
            };
            var service = new CalendarService(initializer);

            // Fetch the list of calendars.
            var calendars = await service.CalendarList.List().ExecuteAsync();

            // Fetch some events from each calendar.
            var fetchTasks = new List<Task<Google.Apis.Calendar.v3.Data.Events>>(calendars.Items.Count);
            foreach (var calendar in calendars.Items)
            {
                var request = service.Events.List(calendar.Id);
                request.MaxResults = MaxEventsPerCalendar;
                request.SingleEvents = true;
                request.TimeMin = DateTime.Now;
                request.TimeMax = DateTime.Now.AddDays(7.0); //maximum events shown is for 7 days
                fetchTasks.Add(request.ExecuteAsync());
            }
            var fetchResults = await Task.WhenAll(fetchTasks);

            // Sort the events and put them in the model.
            var upcomingEvents = from result in fetchResults
                                 from evt in result.Items
                                 where evt.Start != null
                                 let date = evt.Start.DateTime.HasValue ?
                                     evt.Start.DateTime.Value.Date :
                                     DateTime.ParseExact(evt.Start.Date, "yyyy-MM-dd", null)
                                 let sortKey = evt.Start.DateTimeRaw ?? evt.Start.Date
                                 orderby sortKey
                                 select new { evt, date };
            var eventsByDate = from result in upcomingEvents.Take(MaxEventsOverall)
                               group result.evt by result.date into g
                               orderby g.Key
                               select g;

            var eventGroups = new List<CalendarEventGroup>();
            foreach (var grouping in eventsByDate)
            {
                eventGroups.Add(new CalendarEventGroup
                {
                    GroupTitle = grouping.Key.ToLongDateString(),
                    Events = grouping,
                });
            }

            model.EventGroups = eventGroups;

            var initializer2 = new BaseClientService.Initializer()
            {
                HttpClientInitializer = credential,
                ApplicationName = "MetaHealth",
            };
            var service2 = new TasksService(initializer2);

            // Define parameters of request.
            TasklistsResource.ListRequest listRequest = service2.Tasklists.List();
            listRequest.MaxResults = 10;

            string[] listOtasks = new string[10];
            // List task lists.
            IList<TaskList> taskLists = listRequest.Execute().Items;
            if (taskLists != null && taskLists.Count > 0)
            {
                int i = 0;
                foreach (var taskList in taskLists)
                {
                    listOtasks[i] = taskList.Title;
                    i++;
                }
            }

            Google.Apis.Tasks.v1.Data.Tasks tasks = service2.Tasks.List("@default").Execute();
            int amountTask = 0;
            if (tasks.Items != null)
            {
                foreach (var item in tasks.Items)
                {
                    if (item.Status == "needsAction")
                    {
                        amountTask++;
                    }
                }
            }

            string[] taskArr = new string[amountTask];
            string[] taskIDArr = new string[amountTask];
            int indexTask = 0;
            if (tasks.Items != null)
            {
                for (int i = 0; i < tasks.Items.Count; i++)
                {
                    if (tasks.Items[i].Status == "needsAction" && tasks.Items[i].Title != " ")
                    {
                        taskArr[indexTask] = tasks.Items[i].Title;
                        taskIDArr[indexTask] = tasks.Items[i].Id;
                        indexTask++;
                    }
                }
            }

            model.MultiTask = taskArr;
            model.MultiTaskID = taskIDArr;
            model.MultiList = listOtasks;

            bool eventsOrNo = false;

            if (model.EventGroups.Count() == 0)
            {
                eventsOrNo = true;
                ViewBag.NoEvents = eventsOrNo;
            }
            else ViewBag.NoEvents = eventsOrNo;

            return View(model);
        }

        [HttpGet]
        public async Task<ActionResult> MarkDownTask()
        {
            string task = Request.QueryString["task"];
            string taskID = Request.QueryString["taskID"];

            var credential = await GetCredentialForApiAsync();

            var initializer = new BaseClientService.Initializer()
            {
                HttpClientInitializer = credential,
                ApplicationName = "MetaHealth",
            };
            var service = new TasksService(initializer);

            // Define parameters of request.
            TasklistsResource.ListRequest listRequest = service.Tasklists.List();
            listRequest.MaxResults = 10;

            string[] listOtasks = new string[10];
            // List task lists.
            IList<TaskList> taskLists = listRequest.Execute().Items;
            if (taskLists != null)
            {
                int i = 0;
                foreach (var taskList in taskLists)
                {
                    listOtasks[i] = taskList.Title;
                    i++;
                }
            }

            if (taskID != null)
            {
                Google.Apis.Tasks.v1.Data.Task taskObj = service.Tasks.Get("@default", taskID).Execute();
                taskObj.Status = "completed";

                Google.Apis.Tasks.v1.Data.Task result = service.Tasks.Update(taskObj, "@default", taskID).Execute();
            }

            Google.Apis.Tasks.v1.Data.Tasks tasks = service.Tasks.List("@default").Execute();
            int amountTask = 0;
            if (tasks.Items != null)
            {
                foreach (var item in tasks.Items)
                {
                    if (item.Status == "needsAction")
                    {
                        amountTask++;
                    }
                }
            }

            string[,] taskArr = new string[2, amountTask];
            int indexTask = 0;

            if (tasks.Items != null)
            {
                for (int i = 0; i < tasks.Items.Count; i++)
                {
                    if (tasks.Items[i].Status == "needsAction" && tasks.Items[i].Title != " ")
                    {
                        taskArr[1, indexTask] = tasks.Items[i].Title;
                        taskArr[0, indexTask] = tasks.Items[i].Id;
                        indexTask++;
                    }
                }
            }

            var json = JsonConvert.SerializeObject(taskArr);

            return Content(json);
        }

        [HttpPost]
        public async Task<ActionResult> UpcomingEvents(string taskTitle)
        {
            var credential = await GetCredentialForApiAsync();

            //Add a new task
            var initializer3 = new BaseClientService.Initializer()
            {
                HttpClientInitializer = credential,
                ApplicationName = "MetaHealth",
            };
            var service3 = new TasksService(initializer3);

            Google.Apis.Tasks.v1.Data.Tasks tasks = service3.Tasks.List("@default").Execute();

            Google.Apis.Tasks.v1.Data.Task task = new Google.Apis.Tasks.v1.Data.Task { Title = taskTitle };

            Google.Apis.Tasks.v1.Data.Task newTask = service3.Tasks.Insert(task, "@default").Execute();

            UpcomingEventsViewModel model = await GetCurrentEventsTask();

            ModelState.Clear();

            return View(model);
        }

        [HttpPost]
        public async Task<ActionResult> AddPreMadeOne()
        {
            var credential = await GetCredentialForApiAsync();
            var initializer3 = new BaseClientService.Initializer()
            {
                HttpClientInitializer = credential,
                ApplicationName = "MetaHealth",
            };
            var service3 = new TasksService(initializer3);

            Google.Apis.Tasks.v1.Data.Tasks tasks = service3.Tasks.List("@default").Execute();

            Google.Apis.Tasks.v1.Data.Task task = new Google.Apis.Tasks.v1.Data.Task { Title = "Get out of bed" };
            service3.Tasks.Insert(task, "@default").Execute();

            Google.Apis.Tasks.v1.Data.Task task1 = new Google.Apis.Tasks.v1.Data.Task { Title = "Drink a glass of water" };
            service3.Tasks.Insert(task1, "@default").Execute();

            Google.Apis.Tasks.v1.Data.Task task2 = new Google.Apis.Tasks.v1.Data.Task { Title = "Eat a meal" };
            service3.Tasks.Insert(task2, "@default").Execute();

            UpcomingEventsViewModel model = await GetCurrentEventsTask();

            return View("UpcomingEvents", model);
        }

        [HttpPost]
        public async Task<ActionResult> AddPreMadeTwo()
        {
            var credential = await GetCredentialForApiAsync();
            var initializer3 = new BaseClientService.Initializer()
            {
                HttpClientInitializer = credential,
                ApplicationName = "MetaHealth",
            };
            var service3 = new TasksService(initializer3);

            Google.Apis.Tasks.v1.Data.Tasks tasks = service3.Tasks.List("@default").Execute();

            Google.Apis.Tasks.v1.Data.Task task = new Google.Apis.Tasks.v1.Data.Task { Title = "Take a shower" };
            service3.Tasks.Insert(task, "@default").Execute();

            Google.Apis.Tasks.v1.Data.Task task1 = new Google.Apis.Tasks.v1.Data.Task { Title = "Talk to someone" };
            service3.Tasks.Insert(task1, "@default").Execute();

            Google.Apis.Tasks.v1.Data.Task task2 = new Google.Apis.Tasks.v1.Data.Task { Title = "Brush Teeth" };
            service3.Tasks.Insert(task2, "@default").Execute();

            Google.Apis.Tasks.v1.Data.Task task3 = new Google.Apis.Tasks.v1.Data.Task { Title = "Put on Deodorant" };
            service3.Tasks.Insert(task3, "@default").Execute();

            UpcomingEventsViewModel model = await GetCurrentEventsTask();

            return View("UpcomingEvents", model);
        }

        [HttpPost]
        public async Task<ActionResult> AddPreMadeThree()
        {
            var credential = await GetCredentialForApiAsync();
            var initializer3 = new BaseClientService.Initializer()
            {
                HttpClientInitializer = credential,
                ApplicationName = "MetaHealth",
            };
            var service3 = new TasksService(initializer3);

            Google.Apis.Tasks.v1.Data.Tasks tasks = service3.Tasks.List("@default").Execute();

            Google.Apis.Tasks.v1.Data.Task task = new Google.Apis.Tasks.v1.Data.Task { Title = "Make your bed" };
            service3.Tasks.Insert(task, "@default").Execute();

            Google.Apis.Tasks.v1.Data.Task task1 = new Google.Apis.Tasks.v1.Data.Task { Title = "Talk to someone face to face" };
            service3.Tasks.Insert(task1, "@default").Execute();

            Google.Apis.Tasks.v1.Data.Task task2 = new Google.Apis.Tasks.v1.Data.Task { Title = "Go on a walk" };
            service3.Tasks.Insert(task2, "@default").Execute();

            Google.Apis.Tasks.v1.Data.Task task3 = new Google.Apis.Tasks.v1.Data.Task { Title = "Pick up room" };
            service3.Tasks.Insert(task3, "@default").Execute();

            UpcomingEventsViewModel model = await GetCurrentEventsTask();

            return View("UpcomingEvents", model);
        }

        public async Task<UpcomingEventsViewModel> GetCurrentEventsTask()
        {
            const int MaxEventsPerCalendar = 20;
            const int MaxEventsOverall = 50;

            var model = new UpcomingEventsViewModel();

            var credential = await GetCredentialForApiAsync();

            var initializer = new BaseClientService.Initializer()
            {
                HttpClientInitializer = credential,
                ApplicationName = "MetaHealth",
            };
            var service = new CalendarService(initializer);

            // Fetch the list of calendars.
            var calendars = await service.CalendarList.List().ExecuteAsync();

            // Fetch some events from each calendar.
            var fetchTasks = new List<Task<Google.Apis.Calendar.v3.Data.Events>>(calendars.Items.Count);
            foreach (var calendar in calendars.Items)
            {
                var request = service.Events.List(calendar.Id);
                request.MaxResults = MaxEventsPerCalendar;
                request.SingleEvents = true;
                request.TimeMin = DateTime.Now;
                request.TimeMax = DateTime.Now.AddDays(7.0);
                fetchTasks.Add(request.ExecuteAsync());
            }
            var fetchResults = await Task.WhenAll(fetchTasks);

            // Sort the events and put them in the model.
            var upcomingEvents = from result in fetchResults
                                 from evt in result.Items
                                 where evt.Start != null
                                 let date = evt.Start.DateTime.HasValue ?
                                     evt.Start.DateTime.Value.Date :
                                     DateTime.ParseExact(evt.Start.Date, "yyyy-MM-dd", null)
                                 let sortKey = evt.Start.DateTimeRaw ?? evt.Start.Date
                                 orderby sortKey
                                 select new { evt, date };
            var eventsByDate = from result in upcomingEvents.Take(MaxEventsOverall)
                               group result.evt by result.date into g
                               orderby g.Key
                               select g;

            var eventGroups = new List<CalendarEventGroup>();
            foreach (var grouping in eventsByDate)
            {
                eventGroups.Add(new CalendarEventGroup
                {
                    GroupTitle = grouping.Key.ToLongDateString(),
                    Events = grouping,
                });
            }

            model.EventGroups = eventGroups;

            //Tasks
            var initializer3 = new BaseClientService.Initializer()
            {
                HttpClientInitializer = credential,
                ApplicationName = "MetaHealth",
            };
            var service3 = new TasksService(initializer3);

            Google.Apis.Tasks.v1.Data.Tasks tasks = service3.Tasks.List("@default").Execute();

            int amountTask = 0;
            if (tasks.Items != null)
            {
                foreach (var item in tasks.Items)
                {
                    if (item.Status == "needsAction")
                    {
                        if (item.Id != null)
                        {
                            amountTask++;
                        }
                    }
                }
            }

            string[] taskArr = new string[amountTask];
            string[] taskIDArr = new string[amountTask];
            int indexTask = 0;
            if (tasks.Items != null)
            {
                for (int i = 0; i < tasks.Items.Count; i++)
                {
                    if (tasks.Items[i].Status == "needsAction" && tasks.Items[i].Id != null)
                    {
                        taskArr[indexTask] = tasks.Items[i].Title;
                        taskIDArr[indexTask] = tasks.Items[i].Id;
                        indexTask++;
                    }
                }
            }

            model.MultiTask = taskArr;
            model.MultiTaskID = taskIDArr;

            // Define parameters of request.
            var service2 = new TasksService(initializer3);
            TasklistsResource.ListRequest listRequest = service2.Tasklists.List();
            listRequest.MaxResults = 10;

            string[] listOtasks = new string[10];
            // List task lists.
            IList<TaskList> taskLists = listRequest.Execute().Items;
            if (taskLists != null && taskLists.Count > 0)
            {
                int i = 0;
                foreach (var taskList in taskLists)
                {
                    listOtasks[i] = taskList.Title;
                    i++;
                }
            }

            model.MultiList = listOtasks;

            return model;
        }

        [HttpPost]
        public async Task<ActionResult> AddEvent(string EventSummary, string EventLocation, string EventDescription, string EventStartDate, string EventStartTime, string EventEndDate, string EventEndTime)
        {
            DateTime EventStartDateTime = Convert.ToDateTime(EventStartDate).Add(TimeSpan.Parse(EventStartTime));
            DateTime EventEndDateTime = Convert.ToDateTime(EventEndDate).Add(TimeSpan.Parse(EventEndTime));
            var credential = await GetCredentialForApiAsync();

            var initializer = new BaseClientService.Initializer()
            {
                HttpClientInitializer = credential,
                ApplicationName = "MetaHealth",
            };
            var calendarService = new CalendarService(initializer);

            if (calendarService != null)
            {
                var list = calendarService.CalendarList.List().Execute();
                var listcnt = list.Items;
                //var calendar = list.Items.SingleOrDefault(c => c.Summary == CustomCalenderName.Trim());
                var calendarId = "primary";

                Google.Apis.Calendar.v3.Data.Event calendarEvent = new Google.Apis.Calendar.v3.Data.Event();

                calendarEvent.Summary = EventSummary;
                calendarEvent.Location = EventLocation;
                calendarEvent.Description = EventDescription;

                calendarEvent.Start = new Google.Apis.Calendar.v3.Data.EventDateTime
                {
                    DateTime = EventStartDateTime /*new DateTime(StartDate.Year, StartDate.Month, StartDate.Day, StartDate.Hour, StartDate.Minute, StartDate.Second)*/,
                    TimeZone = "America/Los_Angeles"
                };
                calendarEvent.End = new Google.Apis.Calendar.v3.Data.EventDateTime
                {
                    DateTime = EventEndDateTime /*new DateTime(EndDate.Year, EndDate.Month, EndDate.Day, EndDate.Hour, EndDate.Minute, EndDate.Second)*/,
                    TimeZone = "America/Los_Angeles"
                };
                calendarEvent.Recurrence = new List<string>();

                var newEventRequest = calendarService.Events.Insert(calendarEvent, calendarId);
                var eventResult = newEventRequest.Execute();
            }
            UpcomingEventsViewModel model = await GetCurrentEventsTask();
            return View("UpcomingEvents", model);
        }

        public string[] CountingTasks(string[] tasks)
        {
            int amountTask = 0;
            if (tasks != null)
            {
                foreach (var item in tasks)
                {
                    if (item == "needsAction")
                    {
                        amountTask++;
                    }
                }
            }

            string[] taskArr = new string[amountTask];
            int indexTask = 0;
            if (tasks != null)
            {
                for (int i = 0; i < tasks.Length; i++)
                {
                    if (tasks[i] == "needsAction")
                    {
                        taskArr[indexTask] = tasks[i];
                        indexTask++;
                    }
                }
            }

            return (taskArr);
        }
    }

}