﻿using System.Linq;
using SonarPlugin.Game;
using Sonar.Models;
using Sonar.Data;
using Dalamud.Game;
using Dalamud.Game.ClientState.Objects.Types;
using System.Threading;
using System.Threading.Tasks;
using Sonar.Trackers;
using Dalamud.Game.ClientState.Objects;
using Dalamud.Logging;

namespace SonarPlugin.Trackers
{
    public sealed class SonarHuntProvider : IHostedService
    {
        private PlayerProvider Player { get; }
        private HuntTracker Tracker { get; }
        private ObjectTable Table { get; }
        private SonarPlugin Plugin { get; }

        /// <summary>
        /// Initialize monster tracker
        /// </summary>
        public SonarHuntProvider(PlayerProvider player, HuntTracker tracker, ObjectTable table, SonarPlugin plugin)
        {
            // Get Sonar and Plugin Interface
            this.Player = player;
            this.Tracker = tracker;
            this.Table = table;
            this.Plugin = plugin;

            // Initialization feedback
            PluginLog.LogInformation("MobTracker Initialized");
        }

        private void FrameworkTick(Framework framework)
        {
            // Don't proceed if the structures aren't ready
            if (!this.Plugin.SafeToReadTables) return;

            // Iterate throughout all hunts in the actor table
            var hunts = this.Table
                .OfType<BattleNpc>()
                .Where(a => Database.Hunts.ContainsKey(a.NameId))
                .Select(h => h.ToSonarHuntRelay(this.Player.Place, this.Player.PlayerCount));

            this.Tracker.FeedRelays(hunts);
        }

        public Task StartAsync(CancellationToken cancellationToken)
        {
            this.Plugin.OnFrameworkTick += this.FrameworkTick;
            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            this.Plugin.OnFrameworkTick -= this.FrameworkTick;
            return Task.CompletedTask;
        }
    }
}
