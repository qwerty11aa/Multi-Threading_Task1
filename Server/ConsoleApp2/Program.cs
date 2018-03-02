﻿using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;


namespace Server
{

    class Program
    {
        /// <summary>
        /// Class contains Task + Cancellation Token to stop Task when client disconnected
        /// and start new task
        /// </summary>
        class PipeServer
        {
            public Task ServerTask { get; private set; }
            public CancellationTokenSource CancellationToken { get; private set; }

            public PipeServer(Task task, CancellationTokenSource token)
            {
                ServerTask=  task;
                CancellationToken = token;
            }
        }

        private static int _serverThreads = 5;
        private static PipeServer[] _servers = new PipeServer[_serverThreads];
        static void Main(string[] args)
        {
            //start server tasks
            for (int i = 0; i < _serverThreads; i++)
            {
                var ts = new CancellationTokenSource();
                CancellationToken ct = ts.Token;
                var task = Task.Factory.StartNew(StartServer, ct);
                _servers[i]= new PipeServer(task, ts);
            }
            
            //find finished task and restart
            while (true)
            {
                for (int i = 0; i < _serverThreads; i++)
                {
                    if ((_servers[i].ServerTask)!=null && (_servers[i].ServerTask.Status == TaskStatus.RanToCompletion))
                    {
                        _servers[i].CancellationToken.Cancel();
                        Thread.Sleep(250);
                        var ts = new CancellationTokenSource();
                        CancellationToken ct = ts.Token;
                        var task = Task.Factory.StartNew(StartServer, ct);
                        _servers[i] = new PipeServer(task, ts);
                    }
                }
            }
        }

        //server
        private static BlockingCollection<string> messages = new BlockingCollection<string>() { "hello", "hello 1", "bye", "Luke I am your father", "Star wars", "phone", "random message", "you're dead", "monitor", "mouse" };

        static void StartServer()
        {
            NamedPipeServerStream server = new NamedPipeServerStream("PipesOfPiece", PipeDirection.InOut, _serverThreads, PipeTransmissionMode.Message, PipeOptions.Asynchronous);
            int threadId = Thread.CurrentThread.ManagedThreadId;

            Console.WriteLine("Waiting for connetion on thread " + threadId);
            server.WaitForConnection();

            StreamWriter writer = new StreamWriter(server);

            writer.WriteLine("Welcome to server on thread " + threadId);
            writer.Flush();
            server.WaitForPipeDrain();
            StreamReader reader = new StreamReader(server);
            string user = "";
            user = reader.ReadLine();
            Console.WriteLine("");
            Console.WriteLine(user + " connected");

            //sending message history
            for (int i = 0; i < 10; i++)
            {
                try
                {
                    writer.WriteLine(messages.Reverse().ToArray()[i]);
                    writer.Flush();
                    server.WaitForPipeDrain();
                }
                catch (Exception e) 
                {
                    Console.WriteLine(user + " Piper error " +e.Message);
                }
            }

            int messagesCount; //variable to check for new messages

            //start reading task
            var ts = new CancellationTokenSource();
            CancellationToken ct = ts.Token;
            Task.Factory.StartNew(() =>
            {
                try
                {
                    while (true)
                    {
                        var line = reader.ReadLine();
                        if (!String.IsNullOrEmpty(line))
                        {
                            messages.TryAdd(line);
                            Console.WriteLine(line);
                            messagesCount = messages.Count();
                        }
                        else
                        {
                            reader.Close();
                        }
                    }
                }
                catch (Exception e)
                {
                    Console.WriteLine(user + " Pipe error " + e.Message);
                }
            }, ct);

            //start whatching for new messages
            messagesCount = messages.Count();
            try
            {
                while (true)
                {
                    if (messages.Count() > messagesCount)
                    {
                        for (int i = messagesCount; i < messages.Count; i++)
                        {
                            writer.WriteLine(messages.ToArray()[i]);
                            writer.Flush();
                        }
                        server.WaitForPipeDrain();
                        messagesCount = messages.Count();
                    }
                    Thread.Sleep(1000);

                    //randomly kill server
                    Random rnd = new Random();
                    if (rnd.Next(1,25) == 10)
                    {
                        writer.WriteLine("Server Over");
                        writer.Flush();
                        ts.Cancel();
                        server.Close();
                        server.Dispose();
                    }
                }
            }
            catch (Exception)
            {
                Console.WriteLine(user + " Disconnected");
                ts.Cancel();
                server.Close();
                server.Dispose();
            }
        }
    }
}