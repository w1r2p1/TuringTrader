﻿//==============================================================================
// Project:     TuringTrader, algorithms from books & publications
// Name:        Antonacci_DualMomentumInvesting
// Description: Strategy, as published in Gary Antonacci's book
//              'Dual Momentum Investing'.
//              http://www.optimalmomentum.com/
// History:     2018xi22, FUB, created
//------------------------------------------------------------------------------
// Copyright:   (c) 2017-2018, Bertram Solutions LLC
//              http://www.bertram.solutions
// License:     This code is licensed under the term of the
//              GNU Affero General Public License as published by 
//              the Free Software Foundation, either version 3 of 
//              the License, or (at your option) any later version.
//              see: https://www.gnu.org/licenses/agpl-3.0.en.html
//==============================================================================

#region libraries
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TuringTrader.Simulator;
#endregion

namespace TuringTrader.BooksAndPubs
{
    public class Antonacci_DualMomentumInvesting : Algorithm
    {
        #region internal data
        private readonly double INITIAL_FUNDS = 100000;
        private readonly string SPX = "^SPX.Index";
        private Plotter _plotter = new Plotter();
        #endregion
        #region instruments
        private static readonly HashSet<HashSet<string>> _assetClasses = new HashSet<HashSet<string>>
        {
            //--- equity
            new HashSet<string> {
                "VTI.ETF",   // Vanguard Total Stock Market Index ETF
                "VEU.ETF",   // Vanguard FTSE All World ex US ETF
                // could use SPY/ EFA here
                "SHY.etf",   // iShares 1-3 Year Treasury Bond ETF
            },
            //--- credit
            new HashSet<string> {
                "HYG.ETF",   // iShares iBoxx High Yield Corporate Bond ETF
                //"CIU.ETF" => changed to IGIB in 06/2018
                "IGIB.ETF",  // iShares Intermediate-Term Corporate Bond ETF
                "SHY.etf",   // iShares 1-3 Year Treasury Bond ETF
            },
            //--- real estate
            new HashSet<string> {
                "VNQ.ETF",   // Vanguard Real Estate Index ETF
                "REM.ETF",   // iShares Mortgage Real Estate ETF
                "SHY.etf",   // iShares 1-3 Year Treasury Bond ETF
            },
            //--- economic stress
            new HashSet<string> {
                "GLD.ETF",   // SPDR Gold Shares ETF
                "TLT.ETF",   // iShares 20+ Year Treasury Bond ETF
                "SHY.etf",   // iShares 1-3 Year Treasury Bond ETF
            },
        };
        #endregion

        #region public override void Run()
        public override void Run()
        {
            //----- initialization

            StartTime = DateTime.Parse("01/01/1990");
            EndTime = DateTime.Now - TimeSpan.FromDays(3);

            AddDataSource(SPX);
            foreach (HashSet<string> assetClass in _assetClasses)
                foreach (string nick in assetClass)
                    AddDataSource(nick);

            Deposit(INITIAL_FUNDS);
            CommissionPerShare = 0.015; // it is unclear, if the book considers commissions

            _plotter.Clear();

            //----- simulation loop

            foreach (DateTime simTime in SimTimes)
            {
                // evaluate momentum for all known instruments
                // it is not 100% clear, how Antonacci is weighting
                // the 3 individual momentums
                Dictionary<Instrument, double> instrumentMomentum = Instruments
                    .ToDictionary(i => i,
                        i => 0.3333
                            * (4.0 * (i.Close[0] / i.Close[63] - 1.0)
                            + 2.0 * (i.Close[0] / i.Close[126] - 1.0)
                            + 1.0 * (i.Close[0] / i.Close[252] - 1.0)));

                // skip if there are any missing instruments
                // we want to make sure our strategy has all instruments available
                bool instrumentsMissing = _assetClasses
                    .SelectMany(c => c)
                    .Distinct()
                    .Where(n => Instruments.Where(i => i.Nickname == n).Count() == 0)
                    .Count() > 0;

                if (instrumentsMissing)
                    continue;

                // create empty structure for instrument weights
                Dictionary<Instrument, double> instrumentWeights = Instruments
                    .ToDictionary(i => i, i => 0.0);

                // loop through all asset classes
                foreach (HashSet<string> assetClass in _assetClasses)
                {
                    List<Instrument> assetClassInstruments = assetClass
                        .Select(n => FindInstrument(n))
                        .ToList();

                    // find the instrument with the highest momentum
                    // in each asset class
                    var bestInstrument = assetClassInstruments
                        .OrderByDescending(i => instrumentMomentum[i])
                        .Take(1)
                        .First();

                    // sum up the weights (because the safe instrument is duplicated)
                    instrumentWeights[bestInstrument] += 1.0 / _assetClasses.Count;
                }

                // execute trades once per month
                if (SimTime[0].Month != SimTime[1].Month)
                {
                    foreach (var instrumentWeight in instrumentWeights)
                    {
                        int targetShares = (int)Math.Floor(instrumentWeight.Value * NetAssetValue[0] / instrumentWeight.Key.Close[0]);
                        int currentShares = instrumentWeight.Key.Position;

                        Order newOrder = instrumentWeight.Key.Trade(targetShares - currentShares);

                        if (newOrder != null)
                        {
                            if (currentShares == 0) newOrder.Comment = "open";
                            else if (targetShares == 0) newOrder.Comment = "close";
                            else newOrder.Comment = "rebalance";
                        }
                    }
                }

                // create plots on Sheet 1
                if (TradingDays > 0)
                {
                    _plotter.SelectChart(Name, "date");
                    _plotter.SetX(SimTime[0]);
                    _plotter.Plot("NAV", NetAssetValue[0]);
                    _plotter.Plot(SPX, FindInstrument(SPX).Close[0]);
                }
            }

            //----- post processing

            // create trading log on Sheet 2
            _plotter.SelectChart(Name + " trades", "date");
            foreach (LogEntry entry in Log)
            {
                _plotter.SetX(entry.BarOfExecution.Time);
                _plotter.Plot("action", entry.Action);
                _plotter.Plot("type", entry.InstrumentType);
                _plotter.Plot("instr", entry.Symbol);
                _plotter.Plot("qty", entry.OrderTicket.Quantity);
                _plotter.Plot("fill", entry.FillPrice);
                _plotter.Plot("gross", -entry.OrderTicket.Quantity * entry.FillPrice);
                _plotter.Plot("commission", -entry.Commission);
                _plotter.Plot("net", -entry.OrderTicket.Quantity * entry.FillPrice - entry.Commission);
                _plotter.Plot("comment", entry.OrderTicket.Comment ?? "");
            }
        }
        #endregion
        #region public override void Report()
        public override void Report()
        {
            _plotter.OpenWith("SimpleReport");
        }
        #endregion
    }
}

//==============================================================================
// end of file