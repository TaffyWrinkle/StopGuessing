﻿using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace PostSimulationAnalysisOldRuntime
{

    public static class Program
    {

        private static List<Trial> LoadData(string path)
        {
            List<Trial> trials = new List<Trial>();
            using (StreamReader file = new StreamReader(path))
            {
                // Skip header
                file.ReadLine();

                string line;
                while ((line = file.ReadLine()) != null)
                {
                    string[] fields = line.Trim().Split(new char[] { ',' });
                    if (fields.Length >= 11)
                        trials.Add(new Trial(fields));
                }
            }
            return trials;
        }



        public static void Main()   // string[] args
        {
            string path = @"E:\Experiment_1_22_16_25\";
            List<Trial> trials = LoadData(path + "data.txt");

            List<Trial> trialsWithCorrectPassword = trials.Where(t => t.IsPasswordCorrect).ToList();
            List<Trial> trialsUsersCorrectPassword = trialsWithCorrectPassword.Where(t => !t.IsFromAttacker || !t.IsAGuess).ToList();
            List<Trial> trialsGuessesCorrectPassword = trialsWithCorrectPassword.Where(t => t.IsFromAttacker && t.IsAGuess).ToList();
            int numConditions = trialsWithCorrectPassword.First().scoreForEachCondition.Length;

            //long numCorrectBenignFromAttackersIPpool = trialsUsersCorrectPassword.LongCount(t => t.IsIpInAttackersPool);
            //long numCorrectBenignFromProxy = trialsUsersCorrectPassword.LongCount(t => t.IsClientAProxyIP);
            //long numCorrectBenignBoth = trialsUsersCorrectPassword.LongCount(t => t.IsIpInAttackersPool && t.IsClientAProxyIP);
            long numCorrectGuessesFromProxy = trialsGuessesCorrectPassword.LongCount(t => t.IsClientAProxyIP);
            long numCorrectGuessesFromBenignIpPool = trialsGuessesCorrectPassword.LongCount(t => t.IsIpInBenignPool);
            long numCorrectGuessesFromBoth = trialsGuessesCorrectPassword.LongCount(t => t.IsIpInBenignPool && t.IsClientAProxyIP);

            for (int conditionNumber = 0; conditionNumber < numConditions; conditionNumber++)
            {
                using (StreamWriter writer = new StreamWriter(path + "PointsFor_" + conditionNumber.ToString() + ".csv"))
                {
                    writer.WriteLine("{0},{1},{2},{3},{4},{5},{6},{7},{8},{9},{10},{11},{12},{13},{14}",
                        "False +",
                        "False -",
                        "True +",
                        "True -",
                        "FP Rate",
                        "TP Rate",
                        "Precision",
                        "Recall",
                        "FPwAttackerIP",
                        "FPwProxy",
                        "FPwBoth",
                        "FNwBenignIP",
                        "FNwProxy",
                        "FNwBoth",
                        "Threshold");

                    List<Trial> originalMalicious = new List<Trial>(trialsGuessesCorrectPassword);
                    List<Trial> originalBenign = new List<Trial>(trialsUsersCorrectPassword);
                    originalMalicious.Sort((a, b) => -a.CompareTo(b, conditionNumber));
                    originalBenign.Sort((a, b) => -a.CompareTo(b, conditionNumber));

                    Queue<Trial> malicious = new Queue<Trial>(originalMalicious);
                    Queue<Trial> benign = new Queue<Trial>(originalBenign);

                    int falsePositivesWithProxy = 0;
                    int falsePositivesWithAttackerIp = 0;
                    int falsePositivesWithProxyAndAttackerIp = 0;
                    long falseNegativeWithProxy = numCorrectGuessesFromProxy;
                    long falseNegativeBenignIp = numCorrectGuessesFromBenignIpPool;
                    long falseNegativeBenignProxyIp = numCorrectGuessesFromBoth;
                    //int falseNegativeFromDefendersIpPool;


                    double blockThreshold = malicious.Peek().GetScoreForCondition(conditionNumber);
                    List<ROCPoint> rocPoints = new List<ROCPoint>
                    {
                        new ROCPoint(0, 0, originalMalicious.Count, originalBenign.Count,
                        falsePositivesWithAttackerIp, falsePositivesWithProxy, falsePositivesWithProxyAndAttackerIp,
                        falseNegativeBenignIp, falseNegativeWithProxy, falseNegativeBenignProxyIp,
                        blockThreshold)
                    };
                    while (malicious.Count > 0)
                    {
                        // Remove all malicious requests above this new threshold
                        while (malicious.Count > 0 && malicious.Peek().GetScoreForCondition(conditionNumber) >= blockThreshold)
                        {
                            Trial t = malicious.Dequeue();
                            if (t.IsClientAProxyIP)
                                falseNegativeWithProxy--;
                            if (t.IsIpInBenignPool)
                                falseNegativeBenignIp--;
                            if (t.IsIpInBenignPool && t.IsClientAProxyIP)
                                falseNegativeBenignProxyIp--;
                        }

                        // Remove all benign requests above this new threshold
                        while (benign.Count > 0 && benign.Peek().GetScoreForCondition(conditionNumber) >= blockThreshold)
                        {
                            Trial t = benign.Dequeue();
                            if (t.IsIpInAttackersPool)
                                falsePositivesWithAttackerIp++;
                            if (t.IsClientAProxyIP)
                                falsePositivesWithProxy++;
                            if (t.IsIpInAttackersPool && t.IsClientAProxyIP)
                                falsePositivesWithProxyAndAttackerIp++;
                        }

                        rocPoints.Add(new ROCPoint(originalBenign.Count - benign.Count, originalMalicious.Count - malicious.Count,
                            malicious.Count, benign.Count,
                            falsePositivesWithAttackerIp, falsePositivesWithProxy, falsePositivesWithProxyAndAttackerIp,
                            falseNegativeBenignIp, falseNegativeWithProxy, falseNegativeBenignProxyIp,
                            blockThreshold));

                        // Identify next threshold
                        if (malicious.Count > 0)
                            blockThreshold = malicious.Peek().GetScoreForCondition(conditionNumber);
                    }

                    List<ROCPoint> finalROCPoints = new List<ROCPoint>();
                    Queue<ROCPoint> rocPointQueue = new Queue<ROCPoint>(rocPoints);
                    while (rocPointQueue.Count > 0)
                    {
                        ROCPoint point = rocPointQueue.Dequeue();
                        if (rocPointQueue.Count == 0 || rocPointQueue.Peek().FalsePositives > point.FalsePositives)
                            finalROCPoints.Add(point);
                    }

                    foreach (ROCPoint point in finalROCPoints)
                    {
                        writer.WriteLine("{0},{1},{2},{3},{4},{5},{6},{7},{8},{9},{10},{11},{12},{13},{14}",
                            point.FalsePositives, point.FalseNegatives, point.TruePositives, point.TrueNegatives,
                            point.FalsePositiveRate, point.TruePositiveRate, point.Precision, point.Recall,
                            point.FalsePositivesWithAttackerIp, point.FalsePositivesWithProxy, point.FalsePositivesWithProxyAndAttackerIp,
                            point.FalseNegativesWithBenignIp, point.FalseNegativesWithProxy, point.FalseNegativesWithBenignProxyIp,
                            point.BlockingThreshold);
                    }
                    writer.Flush();


                }
            }
        }
    }
}