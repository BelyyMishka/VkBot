using System;
using System.Collections.ObjectModel;
using System.Threading;
using System.Threading.Tasks;
using VkNet;
using VkNet.Enums;
using VkNet.Exception;
using VkNet.Model;
using VkNet.Model.RequestParams;
using HtmlAgilityPack;

namespace MonteceVkBot
{
    class Program
    {
        static VkApi vkapi = new VkApi();
        static long userID = 0;
        static ulong? Ts;
        static ulong? Pts;
        static bool IsActive;
        static Timer WatchTimer = null;
        static byte MaxSleepSteps = 3;
        static int StepSleepTime = 333;
        static byte CurrentSleepSteps = 1;
        delegate void MessagesRecievedDelegate(VkApi owner, ReadOnlyCollection<Message> messages);
        static event MessagesRecievedDelegate NewMessages;
     
        static void Main(string[] args)
        {
            Console.Title = "VkBot";
            // const string KEY = "83bef185aa03b428bc23f609c7341762b4aad6735978f64a740f0e8f195f5c8b8f86ec79ed5d5a20b8099";
            Console.WriteLine("Попытка авторизации...");
            if (Auth(KEY))
            {
                ColorMessage("Авторизация успешно завершена.", ConsoleColor.Green);
                Eye();
            }
            else
            {
                ColorMessage("Не удалось произвести авторизацию!", ConsoleColor.Red);
            }
            Console.WriteLine("Нажмите ENTER чтобы выйти...");
            Console.ReadLine();
        }        

        static void ColorMessage(string text, ConsoleColor color)
        {
            Console.ForegroundColor = color;
            Console.WriteLine(text);
            Console.ForegroundColor = ConsoleColor.Gray;
        }

        static bool Auth(string GroupID)
        {
            try
            {
                vkapi.Authorize(new ApiAuthParams { AccessToken = GroupID });
                return true;
            }
            catch (Exception e)
            {
                Console.WriteLine(e.Message);
                return false;
            }
        }

        static void Command(string Message)
        {
            Message = Message.ToLower();
            switch(Message)
            {
                case "!помощь":
                    SendMessage("Список доступных команд:" + Environment.NewLine + "!Новости" + Environment.NewLine + "!Погода");
                    break;
                case "!новости":
                    ReadNews();
                    break;
                case "!погода":
                    Weather();
                    break;
                default:
                    SendMessage("Неизвестная команда. Напиши '!Помощь', чтобы посмотреть список доступных команд.");
                    break;
            }           
        }

        static void SendMessage(string Body)
        {
            try
            {
                vkapi.Messages.Send(new MessagesSendParams
                {
                    UserId = userID,
                    Message = Body
                });
            }
            catch(Exception e)
            {
                ColorMessage("Ошибка! " + e.Message, ConsoleColor.Red);
            }
            
        }

        static void Eye()
        {
            LongPollServerResponse Pool = vkapi.Messages.GetLongPollServer(true);
            StartAsync(Pool.Ts, Pool.Pts);
            NewMessages += Watcher_NewMessages;
        }

        static void Watcher_NewMessages(VkApi owner, ReadOnlyCollection<Message> messages)
        {
            for (int i = 0; i < messages.Count; i++)
            {
                if (messages[i].Type != MessageType.Sended)
                {
                    User Sender = vkapi.Users.Get(messages[i].UserId.Value);
                    userID = messages[i].UserId.Value;
                    Command(messages[i].Body);
                }
            }
        }

        static LongPollServerResponse GetLongPoolServer(ulong? lastPts = null)
        {
            LongPollServerResponse response = vkapi.Messages.GetLongPollServer(false, lastPts == null);
            Ts = response.Ts;
            Pts = Pts == null ? response.Pts : lastPts;
            return response;
        }

        static Task<LongPollServerResponse> GetLongPoolServerAsync(ulong? lastPts = null)
        {
            return Task.Run(() => 
            {
                return GetLongPoolServer(lastPts);
            });
        }

        static LongPollHistoryResponse GetLongPoolHistory()
        {
            if (!Ts.HasValue) GetLongPoolServer(null);
            MessagesGetLongPollHistoryParams rp = new MessagesGetLongPollHistoryParams();
            rp.Ts = Ts.Value;
            rp.Pts = Pts;
            int i = 0;
            LongPollHistoryResponse history = null;
            string errorLog = "";
            while (i < 5 && history == null)
            {
                i++;
                try
                {
                    history = vkapi.Messages.GetLongPollHistory(rp);
                }
                catch (TooManyRequestsException)
                {
                    Thread.Sleep(150);
                    i--;
                }
                catch (Exception ex)
                {                    
                    errorLog += string.Format("{0} - {1}{2}", i, ex.Message, Environment.NewLine);
                }
            }

            if (history != null)
            {
                Pts = history.NewPts;
                foreach (var m in history.Messages)
                {
                    m.FromId = m.Type == MessageType.Sended ? vkapi.UserId : m.UserId;
                }                    
            }
            else ColorMessage(errorLog, ConsoleColor.Red);
            return history;
        }

        static Task<LongPollHistoryResponse> GetLongPoolHistoryAsync()
        {
            return Task.Run(() => { return GetLongPoolHistory(); });
        }

        static async void WatchAsync(object state)
        {
            LongPollHistoryResponse history = await GetLongPoolHistoryAsync();
            if (history.Messages.Count > 0)
            {
                CurrentSleepSteps = 1;
                NewMessages?.Invoke(vkapi, history.Messages);
            }
            else if (CurrentSleepSteps < MaxSleepSteps) CurrentSleepSteps++;
            WatchTimer.Change(CurrentSleepSteps * StepSleepTime, Timeout.Infinite);
        }

        static async void StartAsync(ulong? lastTs = null, ulong? lastPts = null)
        {
            IsActive = true;
            await GetLongPoolServerAsync(lastPts);
            WatchTimer = new Timer(new TimerCallback(WatchAsync), null, 0, Timeout.Infinite);
        }

        static void Weather()
        {
            string url = "https://krasnoyarsk.nuipogoda.ru";
            HtmlWeb webDoc = new HtmlWeb();
            HtmlDocument doc = webDoc.Load(url);
            HtmlNode par = doc.DocumentNode.SelectSingleNode("//*[@id='loading']/div[1]/div[1]/text()");
            HtmlNode wea = doc.DocumentNode.SelectSingleNode("//*[@id='loading']/div[1]/div[2]");
            string temperature = par.InnerHtml;
            int position = temperature.LastIndexOf("&");
            temperature = temperature.Substring(0, position);
            var description = wea.InnerText;
            SendMessage("Погода в Красноярске: " + temperature + " градусов, " + description + ".");
        }

        static void ReadNews()
        {
            string news = "Последние новости в мире:" + Environment.NewLine;
            string url = "https://ria.ru/lenta/";
            HtmlWeb webDoc = new HtmlWeb();
            HtmlDocument doc = webDoc.Load(url);
            news += OneNews("//*[@id='wrPage']/div[3]/div[2]/div[3]/div[2]/div[3]/div/div/div[1]/div/div[2]/div[1]/div[1]/a/span[2]/span", doc, 1);
            news += OneNews("//*[@id='wrPage']/div[3]/div[2]/div[3]/div[2]/div[3]/div/div/div[1]/div/div[2]/div[1]/div[2]/a/span[2]/span", doc, 2);
            news += OneNews("//*[@id='wrPage']/div[3]/div[2]/div[3]/div[2]/div[3]/div/div/div[1]/div/div[2]/div[1]/div[3]/a/span[2]/span", doc, 3);
            SendMessage(news);
        }

        static string OneNews(string path, HtmlDocument doc, int number)
        {
            HtmlNode News = doc.DocumentNode.SelectSingleNode(path);
            string tmp = number.ToString() + ") " + News.InnerHtml + Environment.NewLine;
            return tmp;
        }
    }
}