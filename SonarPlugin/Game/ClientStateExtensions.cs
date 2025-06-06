﻿using Dalamud.Game.ClientState.Fates;
using Dalamud.Game.ClientState.Objects.Types;
using Sonar.Enums;
using Sonar.Models;
using Sonar.Relays;
using Sonar.Utilities;
using static Sonar.SonarConstants;

namespace SonarPlugin.Game
{
    public static class ClientStateExtensions
    {
        public static HuntRelay ToSonarHuntRelay(this IBattleNpc mob, GamePlace place, int playerCount)
        {
            return new HuntRelay()
            {
                Id = mob.NameId,
                ActorId = mob.EntityId, // NOTE / TODO: .GameObjectId seem to be something else
                WorldId = place.WorldId,
                ZoneId = place.ZoneId,
                InstanceId = place.InstanceId,
                Coords = mob.Position.SwapYZ(),
                CurrentHp = mob.CurrentHp,
                MaxHp = mob.MaxHp,
                Players = playerCount,
                CheckTimestamp = UnixTimeHelper.SyncedUnixNow,
            };
        }

        public static FateStatus ToSonarFateStatus(this FateState state)
        {
            // Based on: https://github.com/aers/FFXIVClientStructs/blob/main/FFXIVClientStructs/FFXIV/Client/Game/Fate/FateContext.cs
            return state switch
            {
                (FateState)3 => FateStatus.Preparation,
                (FateState)4 => FateStatus.Running,
                (FateState)5 => FateStatus.Running,
                (FateState)7 => FateStatus.Complete,
                (FateState)8 => FateStatus.Failed,

                (FateState)0 => FateStatus.Preparation,     /* Just spawned / Uninitialized */
                (FateState)9 => FateStatus.Failed,          /* Expired? */
                (FateState)6 => FateStatus.Failed,          /* Expired? */

                _ => FateStatus.Unknown,
            };

            // https://github.com/SapphireServer/Sapphire/blob/d9777084c55ac686f8027eaa09c7ad93f8c94f88/src/common/Common.h#L769
            // https://github.com/SapphireServer/Sapphire/blob/master/src/common/Common.h#L781

            // Some fates are expiring with 0x09

            // Fates that just spawned (and still don't have any info) have a Start Time, Duration and State of 0
            // as those values are not initialized yet.
        }


        public static FateRelay ToSonarFateRelay(this IFate fate, GamePlace place, int playerCount)
        {
            return new FateRelay()
            {
                Id = fate.FateId,
                WorldId = place.WorldId,
                ZoneId = place.ZoneId,
                InstanceId = place.InstanceId,
                Coords = fate.Position.SwapYZ(),
                StartTime = fate.StartTimeEpoch * EarthSecond,
                Duration = fate.Duration * EarthSecond,
                Progress = fate.Progress,
                Status = fate.State.ToSonarFateStatus(),
                Players = playerCount,
                CheckTimestamp = UnixTimeHelper.SyncedUnixNow,
                Bonus = fate.HasBonus,
            };
        }
    }
}
