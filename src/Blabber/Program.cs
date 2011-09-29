using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using MongoDB.Driver;
using MongoDB.Driver.Builders;

namespace Blabber
{
    class Program
    {
        static readonly Regex TagsRegex = new Regex(@"(?:(?<=\s)|^)#(\w*[A-Za-z_]+\w*)");
        static readonly Regex MentionsRegex = new Regex(@"(?:(?<=\s)|^)\@(\w*[A-Za-z_]+\w*)");
        static MongoDatabase mongoDatabase;
        static string username;

        static void Main()
        {
            try
            {
                Run();
            }
            catch(Exception e)
            {
                Console.WriteLine(e);
            }
        }

        static void Run()
        {
            mongoDatabase = MongoDatabase.Create("mongodb://localhost/blabber");
            
            using (var worker = new BackgroundWorker {WorkerSupportsCancellation = true})
            {
                worker.DoWork += DoBackgroundWork;
                worker.RunWorkerAsync();

                RunBlabber();

                worker.CancelAsync();
            }
        }

        static void DoBackgroundWork(object sender, DoWorkEventArgs e)
        {
            while (!e.Cancel)
            {
                var collection = mongoDatabase.GetCollection("blabs");

                try
                {
                    var map = new BsonJavaScript(
                        @"
                            function() {
                                this.tags.forEach(function(tag) { emit(tag, {count: 1}); });
                            }
");
                    var reduce = new BsonJavaScript(
                        @"
                            function(key, values) {
                                var count = 0;
                                values.forEach(function(value) { count += value.count; });
                                return {count: count};
                            }
");
                    var options = MapReduceOptions.SetOutput(MapReduceOutput.Merge("tags"));

                    collection.MapReduce(map, reduce, options);
                }
                catch (Exception ex)
                {
                    Console.WriteLine(ex);
                }

                Thread.Sleep(1000);
            }
        }

        static void RunBlabber()
        {
            Console.Write("Please input your username: ");
            username = Console.ReadLine();

            var exit = false;
            do
            {
                Console.WriteLine("(b) post blab, (t) see tags, (s) show blabs with tag, (m) see mentions, (x) exit");

                var inputChar = char.ToLower(Console.ReadKey(true).KeyChar);

                switch (inputChar)
                {
                    case 'b':
                        PostBlab();
                        break;

                    case 't':
                        ShowTags();
                        break;

                    case 's':
                        ShowBlabsWithTag();
                        break;

                    case 'm':
                        ShowMentions();
                        break;

                    case 'x':
                        exit = true;
                        break;

                    default:
                        Console.WriteLine("?");
                        break;
                }
            } while (!exit);
        }

        static void ShowBlabsWithTag()
        {
            Console.Write("tag> ");
            var tag = Console.ReadLine();

            var blabs = mongoDatabase.GetCollection<Blab>("blabs")
                .Find(Query.EQ("tags", tag));

            foreach (var blab in blabs)
            {
                Console.WriteLine(blab);
            }
        }

        static void ShowMentions()
        {
            var blabs = mongoDatabase.GetCollection<Blab>("blabs")
                .Find(Query.EQ("mentions", username));

            foreach (var blab in blabs)
            {
                Console.WriteLine(blab);
            }
        }

        static void ShowTags()
        {
            var tagsCollection = mongoDatabase.GetCollection("tags");
            var tags = tagsCollection.FindAll();

            foreach (var tag in tags)
            {
                Console.WriteLine("{0}: {1}", tag["_id"], tag["value"].AsBsonDocument["count"]);
            }
        }

        static void PostBlab()
        {
            Console.Write("{0}> ", username);
            var blab = Console.ReadLine();
            Post(blab);
        }

        static void Post(string text)
        {
            var tagsAsStrings = TagsRegex.Matches(text)
                .Cast<Match>()
                .Select(m => m.ToString().Substring(1));

            var mentionsAsStrings = MentionsRegex.Matches(text)
                .Cast<Match>()
                .Select(m => m.ToString().Substring(1));

            var blab =
                new Blab
                    {
                        Text = text,
                        Tags = new HashSet<string>(tagsAsStrings),
                        Mentions = new HashSet<string>(mentionsAsStrings),
                        Username = username,
                    };

            Console.WriteLine(blab);

            mongoDatabase.GetCollection<Blab>("blabs").Insert(blab);
        }
    }

    class Blab
    {
        public Blab()
        {
            Id = ObjectId.GenerateNewId();
        }

        [BsonElement("_id")]
        public ObjectId Id { get; set; }

        [BsonElement("text")]
        public string Text { get; set; }

        [BsonElement("tags")]
        public HashSet<string> Tags { get; set; }

        [BsonElement("mentions")]
        public HashSet<string> Mentions { get; set; }

        [BsonElement("username")]
        public string Username { get; set; }

        public override string ToString()
        {
            return string.Format(@"{0}: ""{1}""", Username, Text);
        }
    }
}
