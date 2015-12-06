using System;
using System.Collections.Generic;
using Akka.Actor;

namespace akka.net_coffeehouse
{
    class Program
    {
        static void Main(string[] args)
        {
            var actorSystem = ActorSystem.Create("Coffeehouse");
            var barista = actorSystem.ActorOf(Props.Create<Barista>(), "Barista");
            var customerJohnny = actorSystem.ActorOf(Props.Create<Customer>(barista), "Johnny");
            var customerAlina = actorSystem.ActorOf(Props.Create<Customer>(barista), "Alina");
            customerJohnny.Tell(new CaffeineWithdrawalWarning());
            customerAlina.Tell(new CaffeineWithdrawalWarning());

            customerJohnny.Tell(new CaffeineWithdrawalWarning());
            customerAlina.Tell(new CaffeineWithdrawalWarning());
            Console.ReadLine();
        }
    }

    public class Barista : ReceiveActor
    {
        public Barista()
        {
            var register = Context.ActorOf(Props.Create<Register>(), "Register");
            Receive<EspressoRequest>(espressoRequest =>
            {
                register.Ask<Receipt>(new Transaction(Article.Espresso))
                        .ContinueWith<object>(_ =>
                        {
                            if (_.Exception is AskTimeoutException)
                            {
                                return new ComebackLater();
                            }
                            
                            return new Tuple<EspressoCup, Receipt>(EspressoCup.Filled, _.Result);
                        })
                        .PipeTo(Sender);
            });
            Receive<ClosingTime>(closingTime =>
            {
                Context.Stop(Self);
            });
        }
    }

    public class ComebackLater
    {
    }

    public class EspressoRequest { }
    public class ClosingTime { }
    public class CaffeineWithdrawalWarning { }
    public enum EspressoCup
    {
        Clean,
        Filled,
        Dirty
    }
    public class Customer : ReceiveActor
    {
        private readonly IActorRef _coffeeSource;

        public Customer(IActorRef coffeeSource)
        {
            _coffeeSource = coffeeSource;
            Context.Watch(_coffeeSource);
            Receive<CaffeineWithdrawalWarning>(_ => _coffeeSource.Tell(new EspressoRequest()));
            Receive<Tuple<EspressoCup, Receipt>>(_ => Console.WriteLine("yay, coffee for {0}", Self));
            Receive<Terminated>(_ => Console.WriteLine("Oh well, let's find another coffeehouse"));
        }
    }

    public class Register : ReceiveActor
    {
        private int _revenue = 0;
        private bool _paperJam = false;
        private Dictionary<Article, int> _prices = new Dictionary<Article, int>
        {
            { Article.Espresso, 150 },
            { Article.Cappuccino, 250 }
        };

        private IActorRef _printer;

        public Register()
        {
            _printer = Context.ActorOf(Props.Create<ReceiptPrinter>(), "Printer");

            Receive<Transaction>(transaction =>
            {
                var price = _prices[transaction.Article];
                var requester = Sender;
                _printer.Ask<Receipt>(new PrintJob(price))
                        .ContinueWith(_ => new Tuple<IActorRef, Receipt>(requester, _.Result))
                        .PipeTo(Self);
            });

            Receive<Tuple<IActorRef, Receipt>>(rec =>
            {
                _revenue += rec.Item2.Price;
                Console.WriteLine("Revenue incremented to {0}", _revenue);
                rec.Item1.Tell(rec.Item2);
            });
        }

        protected override void PostRestart(Exception reason)
        {
            base.PostRestart(reason);
            Console.WriteLine("Restarted, and revenue is {0}", _revenue);
        }

       
    }

    public class ReceiptPrinter : ReceiveActor
    {
        private bool _paperJam = false;
        private Random _random = new Random();
        public ReceiptPrinter()
        {
            Receive<PrintJob>(printJob =>
            {
                Sender.Tell(CreateReceipt(printJob.Price));
            });
        }

        private Receipt CreateReceipt(int price)
        {
            if (_random.Next(0, 100) > 50)
                _paperJam = true;
            if (_paperJam)
                throw new PaperJamException("OMG, not again!");
            return new Receipt(price);
        }
    }

    public class PrintJob
    {
        public int Price { get; private set; }

        public PrintJob(int price)
        {
            Price = price;
        }
    }

    public enum Article
    {
        Espresso,
        Cappuccino
    }

    public class Transaction
    {
        public Article Article { get; private set; }

        public Transaction(Article article)
        {
            Article = article;
        }
    }

    public class Receipt
    {
        public int Price { get; private set; }

        public Receipt(int price)
        {
            Price = price;
        }
    }

    public class PaperJamException : Exception
    {
        public PaperJamException(string message) : base(message)
        {}
    }
}
