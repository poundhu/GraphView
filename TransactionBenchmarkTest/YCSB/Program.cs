﻿using Cassandra;
using GraphView.Transaction;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using TransactionBenchmarkTest.TPCC;

namespace TransactionBenchmarkTest.YCSB
{
    class Program
    {
        private static string[] args;

        static void ExecuteRedisRawTest()
        {
            RedisRawTest.BATCHES = 25000;
            RedisRawTest.REDIS_INSTANCES = 4;

            // ONLY FOR SEPARATE PROGRESS
            RedisRawTest.REDIS_INSTANCES = 1;
            //Console.Write("Input the Redis Id (start from 1): ");
            //string line = Console.ReadLine();
            string line = args[0];
            int redisId = int.Parse(line);
            RedisRawTest.OFFSET = redisId - 1;


            new RedisRawTest().Test();

            Console.Write("Type Enter to close...");
            Console.Read();
        }

        static void RedisBenchmarkTest()
        {
            const int workerCount = 4;
            const int taskCount = 1000000;
            const bool pipelineMode = true;
            const int pipelineSize = 100;

            RedisBenchmarkTest test = new RedisBenchmarkTest(workerCount, taskCount, pipelineMode, pipelineSize);
            test.Setup();
            test.Run();
            test.Stats();
        }

        /// <summary>
        /// For YCSB sync benchmark test
        /// </summary>
        static void YCSBTest()
        {
            const int workerCount = 4;    // 4;
            const int taskCountPerWorker = 2000000;   // 50000;
            const string dataFile = "ycsb_data_lg_r.in";
            const string operationFile = "ycsb_ops_lg_r.in";

            // REDIS VERSION DB
            // VersionDb versionDb = RedisVersionDb.Instance;
            // SINGLETON VERSION DB
            VersionDb versionDb = SingletonVersionDb.Instance();

            YCSBBenchmarkTest test = new YCSBBenchmarkTest(workerCount, taskCountPerWorker, versionDb);

            test.Setup(dataFile, operationFile);
            test.Run();
            test.Stats();
        }

        static void YCSBAsyncTest()
        {
            const int partitionCount = 1;
            const int recordCount = 200000;
            const int executorCount = partitionCount;
            const int txCountPerExecutor = 200000;
            //const bool daemonMode = true;
            const bool daemonMode = false;
            const string dataFile = "ycsb_data_r.in";
            const string operationFile = "ycsb_ops_r.in";
            YCSBAsyncBenchmarkTest.RESHUFFLE = true;
            VersionDb.UDF_QUEUE = false;

            // an executor is responsiable for all flush
            string[] tables =
            {
                YCSBAsyncBenchmarkTest.TABLE_ID,
                VersionDb.TX_TABLE
            };

            // The default mode of versionDb is daemonMode
            SingletonPartitionedVersionDb versionDb = SingletonPartitionedVersionDb.Instance(partitionCount, daemonMode);
            // SingletonVersionDb versionDb = SingletonVersionDb.Instance(executorCount);
            YCSBAsyncBenchmarkTest test = new YCSBAsyncBenchmarkTest(recordCount,
                executorCount, txCountPerExecutor, versionDb, tables);
            test.Setup(dataFile, operationFile);
            test.Run();
            test.Stats();

            Console.WriteLine("Enqueued Requests: {0}", SingletonPartitionedVersionDb.EnqueuedRequests);
            //versionDb.Active = false;
        }

        internal static void PinThreadOnCores()
        {
            Thread.BeginThreadAffinity();
            Process Proc = Process.GetCurrentProcess();
            foreach (ProcessThread pthread in Proc.Threads)
            {
                if (pthread.Id == AppDomain.GetCurrentThreadId())
                {
                    long AffinityMask = (long)Proc.ProcessorAffinity;
                    AffinityMask &= 0x0010;
                    // AffinityMask &= 0x007F;
                    pthread.ProcessorAffinity = (IntPtr)AffinityMask;
                }
            }

            Thread.EndThreadAffinity();
        }

        // args[0]: dataFile
        // args[1]: opsFile
        // args[2]: partitionCount
        // args[3]: txCountPerExecutor
        static void YCSBAsyncTestWithSingletonVersionDb(string[] args)
        {
            int partitionCount = 12;
            int executorCount = partitionCount;
            int txCountPerExecutor = 2000000;

            // 20w
            string dataFile = "ycsb_data_r.in";
            const int recordCount = 200000;
            //100w
            //string dataFile = "ycsb_data_m_r.in";
            //const int recordCount = 1000000;
            // 500w
            //string dataFile = "ycsb_data_lg_r.in";
            //const int recordCount = 5000000;
            // 1000w
            //string dataFile = "ycsb_data_hg_r.in";
            //const int recordCount = 10000000;


            string operationFile = "ycsb_ops_r.in";
            if (args.Length > 1)
            {
                dataFile = args[0];
                operationFile = args[1];
                partitionCount = Int32.Parse(args[2]);
                executorCount = partitionCount;
                txCountPerExecutor = args.Length > 3 ? Int32.Parse(args[3]) : txCountPerExecutor;
            }

            // these three settings are useless in SingletonVersionDb environment.
            const bool daemonMode = false;
            const bool insert = false;
            YCSBAsyncBenchmarkTest.RESHUFFLE = false;

            // create all version entries
            if (insert)
            {
                Console.WriteLine("create all version entries");
                int total = partitionCount * txCountPerExecutor;
                TransactionExecutor.versionEntryArray = new VersionEntry[total];
                TransactionExecutor.dummyVersionEntryArray = new VersionEntry[total];
                for (int i = 0; i < total; i++)
                {
                    TransactionExecutor.versionEntryArray[i] = new VersionEntry(null, -1, new String('a', 100), -1);
                    TransactionExecutor.dummyVersionEntryArray[i] = new VersionEntry(-1, VersionEntry.VERSION_KEY_STRAT_INDEX,
                VersionEntry.EMPTY_RECORD, VersionEntry.EMPTY_TXID);

                }
                Console.WriteLine("create version entries finished");
            }

            string[] tables =
            {
                YCSBAsyncBenchmarkTest.TABLE_ID,
                VersionDb.TX_TABLE
            };

            int currentExecutorCount = 1;

            SingletonVersionDb versionDb = SingletonVersionDb.Instance(executorCount);
            YCSBAsyncBenchmarkTest test = new YCSBAsyncBenchmarkTest(recordCount,
                currentExecutorCount, txCountPerExecutor, versionDb, tables);

            for (; currentExecutorCount <= partitionCount; currentExecutorCount++)
            {
                if (currentExecutorCount == 1)
                {
                    test.Setup(dataFile, operationFile);
                }
                else
                {
                    if (insert)
                    {
                        versionDb.Clear();
                    }
                    test.ResetAndFillWorkerQueue(operationFile, currentExecutorCount);
                }
                test.Run();
                test.Stats();
            }
        }

        static void YCSBAsyncTestWithPartitionedVersionDb(string[] args)
        {
            int partitionCount = 2;
            int executorCount = partitionCount;
            int txCountPerExecutor = 1000000;

            // 20w
            string dataFile = "ycsb_data_r.in";
            const int recordCount = 200000;
            //100w
            //string dataFile = "ycsb_data_m_r.in";
            //const int recordCount = 1000000;
            // 500w
            //string dataFile = "ycsb_data_lg_r.in";
            //const int recordCount = 5000000;
            // 1000w
            //string dataFile = "ycsb_data_hg_r.in";
            //const int recordCount = 10000000;


            string operationFile = "ycsb_ops_r.in";
            if (args.Length > 1)
            {
                dataFile = args[0];
                operationFile = args[1];
                partitionCount = Int32.Parse(args[2]);
                executorCount = partitionCount;
                txCountPerExecutor = args.Length > 3 ? Int32.Parse(args[3]) : txCountPerExecutor;
            }

            // these three settings are useless in SingletonVersionDb environment.
            const bool insert = false;
            const bool daemonMode = true;
            YCSBAsyncBenchmarkTest.RESHUFFLE = false;

            if (insert)
            {
                Console.WriteLine("create all version entries");
                int total = partitionCount * txCountPerExecutor;
                TransactionExecutor.versionEntryArray = new VersionEntry[total];
                TransactionExecutor.dummyVersionEntryArray = new VersionEntry[total];
                for (int i = 0; i < total; i++)
                {
                    TransactionExecutor.versionEntryArray[i] = new VersionEntry(null, -1, new String('a', 100), -1);
                    TransactionExecutor.dummyVersionEntryArray[i] = new VersionEntry(-1, VersionEntry.VERSION_KEY_STRAT_INDEX,
               VersionEntry.EMPTY_RECORD, VersionEntry.EMPTY_TXID);
                }
                Console.WriteLine("create version entries finished");
            }

            string[] tables =
            {
                YCSBAsyncBenchmarkTest.TABLE_ID,
                VersionDb.TX_TABLE
            };

            SingletonPartitionedVersionDb versionDb = SingletonPartitionedVersionDb.Instance(1, true);
            YCSBAsyncBenchmarkTest test = new YCSBAsyncBenchmarkTest(recordCount,
                1, txCountPerExecutor, versionDb, tables);

            int currentExecutorCount = 1;
            for (; currentExecutorCount <= partitionCount; currentExecutorCount++)
            {
                if (currentExecutorCount == 1)
                {
                    test.Setup(dataFile, operationFile);
                }
                else
                {
                    if (insert)
                    {
                        versionDb.Clear();
                    }
                    versionDb.ExtendPartition(currentExecutorCount);
                    Console.WriteLine("Extend Partition Finished");
                    test.ResetAndFillWorkerQueue(operationFile, currentExecutorCount);
                }
                test.Run();
                test.Stats();
            }
        }

        //private static bool TEST_ACTIVE = true;
       
        //private static void TestRequestQueue()
        //{
        //    RequestQueue<string> strQueue = new RequestQueue<string>(8);
        //    long beginTicks = DateTime.Now.Ticks;
        //    TEST_ACTIVE = true;

        //    for (int i = 0; i < 8; i++)
        //    {
        //        Task.Factory.StartNew(TestEnqueue, strQueue);
        //    }
        //    Task.Factory.StartNew(TestDequeue, strQueue);

        //    while (DateTime.Now.Ticks - beginTicks < 1 * 10000000) ;
        //    TEST_ACTIVE = false;
        //}

        //private static Action<object> TestEnqueue = (object obj) =>
        //{
        //    RequestQueue<string> strQueue = obj as RequestQueue<string>;
        //    Random rand = new Random();
        //    while (TEST_ACTIVE)
        //    {
        //        int pk = rand.Next(0, 8);
        //        strQueue.Enqueue("123", pk);
        //    }
        //};

        //private static Action<object> TestDequeue = (object obj) =>
        //{
        //    RequestQueue<string> strQueue = obj as RequestQueue<string>;
        //    Random rand = new Random();
        //    string value = null;
        //    while (TEST_ACTIVE)
        //    {
        //        int pk = rand.Next(0, 8);
        //        if (strQueue.TryDequeue(out value))
        //        {
        //            Debug.Assert(value != null);
        //        }
        //    }
        //};

        public static void Main(string[] args)
        {
            Program.args = args;
            // For the YCSB sync test
            // YCSBTest();
            // YCSBSyncTestWithCassandra();
            // test_cassandra();

            // For the redis benchmark Test
            // RedisBenchmarkTest();

            // For the YCSB async test
            // YCSBAsyncTest();
            YCSBAsyncTestWithSingletonVersionDb(args);
            // YCSBAsyncTestWithPartitionedVersionDb(args);
            // YCSBAsyncTestWithCassandra();

            // ExecuteRedisRawTest();
        }
    }
}
