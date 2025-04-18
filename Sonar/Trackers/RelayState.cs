﻿using MessagePack;
using System;
using System.Linq;
using System.Collections.Generic;
using static Sonar.Utilities.UnixTimeHelper;
using Sonar.Messages;
using EditorBrowsableAttribute = System.ComponentModel.EditorBrowsableAttribute;
using EditorBrowsableState = System.ComponentModel.EditorBrowsableState;
using Sonar.Numerics;
using System.Runtime.CompilerServices;
using System.Threading;
using Sonar.Indexes;
using Sonar.Relays;
using Sonar.Data.Rows;
using System.Collections.Frozen;
using SonarUtils.Text.Placeholders.Providers;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;

namespace Sonar.Trackers
{
    /// <summary>
    /// Represents a relay with state information. This class is managed by <see cref="RelayTracker{T}"/> and its not intended to be created manually.
    /// </summary>
    [MessagePackObject]
    [Union(0, typeof(RelayState))]
    [Serializable]
    [SuppressMessage("Naming", "CA1721", Justification = "GetLastSeen, Found and Killed are Datetime getters")]
    [SuppressMessage("Major Code Smell", "S4035", Justification = "Intended. RelayState<T> is sealed.")]
    public abstract class RelayState : ISonarMessage, ITrackerIndexable, IEquatable<RelayState>
    {
        /// <summary>
        /// Grace period before a dead <see cref="RelayState"/> can be alive again
        /// </summary>
        public const double TimeBeforeAlive = Sonar.SonarConstants.EarthSecond * 15;

        /// <summary>
        /// Grace period before a stale <see cref="RelayState"/> can be updated again
        /// </summary>
        public const double TimeBeforeStale = Sonar.SonarConstants.EarthSecond * 15;

        internal SpinLock _lock = new(false);

        protected RelayState(Relay relay)
        {
            this.Relay = relay;
        }

        protected RelayState(Relay relay, double now)
        {
            this.Relay = relay;
            this.LastSeen = now;
            if (this.Relay.IsAliveInternal())
            {
                this.LastFound = now;
                if (relay.IsUntouched()) this.LastUntouched = now;
            }
            else
            {
                this.LastKilled = now;
            }
        }

        #region Relay Properties (forwarded)
        /// <summary>Relay Key</summary>
        [JsonIgnore]
        [IgnoreMember]
        public string RelayKey => this.Relay.RelayKey;

        /// <summary>Place Key</summary>
        [JsonIgnore]
        [IgnoreMember]
        public string PlaceKey => this.Relay.PlaceKey;

        /// <summary>Index Keys</summary>
        [JsonIgnore]
        [IgnoreMember]
        public IEnumerable<string> IndexKeys => this.Relay.IndexKeys;

        [JsonIgnore]
        internal FrozenSet<string> IndexKeysCore => this.Relay.IndexKeysCore;

        /// <summary>Sort Key</summary>
        [JsonIgnore]
        [IgnoreMember]
        public string SortKey => this.Relay.SortKey;

        /// <summary>Relay information</summary>
        [JsonIgnore]
        [IgnoreMember]
        public IRelayDataRow Info => this.Relay.Info;

        /// <summary>Relay ID</summary>
        [JsonIgnore]
        [IgnoreMember]
        public uint Id => this.Relay.Id;

        /// <summary>World ID</summary>
        [JsonIgnore]
        [IgnoreMember]
        public uint WorldId => this.Relay.WorldId;

        /// <summary>Place ID</summary>
        [JsonIgnore]
        [IgnoreMember]
        public uint ZoneId => this.Relay.ZoneId;

        /// <summary>Instance ID</summary>
        [JsonIgnore]
        [IgnoreMember]
        public uint InstanceId => this.Relay.InstanceId;
        #endregion

        #region Properties
        /// <summary>Last Seen</summary>
        [IgnoreMember]
        public double LastSeen { get; set; }

        /// <summary>Relay this state is for</summary>
        [IgnoreMember]
        public Relay Relay { get; set; }

        [Key(1)]
        [EditorBrowsable(EditorBrowsableState.Never)]
        [JsonIgnore]
        public long msgPackLastSeen
        {
            get => unchecked((long)this.LastSeen);
            set => this.LastSeen = value;
        }

        /// <summary>Last Found</summary>
        [IgnoreMember]
        public double LastFound { get; set; } // Updated once something is found for the first time

        [Key(2)]
        [EditorBrowsable(EditorBrowsableState.Never)]
        [JsonIgnore]
        public long msgPackLastFound
        {
            get => unchecked((long)this.LastFound);
            set => this.LastFound = value;
        }
        /// <summary>Last Killed</summary>
        [IgnoreMember]
        public double LastKilled { get; set; } // Updated once killed

        [Key(3)]
        [EditorBrowsable(EditorBrowsableState.Never)]
        [JsonIgnore]
        public long msgPackLastKilled
        {
            get => unchecked((long)this.LastKilled);
            set => this.LastKilled = value;
        }

        /// <summary>Last Updated. This is a synonym of LastSeen.</summary>
        [IgnoreMember]
        public double LastUpdated => this.LastSeen;

        /// <summary>Last Untouched</summary>
        [IgnoreMember]
        public double LastUntouched { get; set; }

        [Key(4)]
        [EditorBrowsable(EditorBrowsableState.Never)]
        [JsonIgnore]
        public long msgPackLastUntouched
        {
            get => unchecked((long)this.LastUntouched);
            set => this.LastUntouched = value;
        }
        #endregion

        #region Ago properties
        /// <summary>Last Seen Ago</summary>
        [IgnoreMember]
        [JsonIgnore]
        public double LastSeenAgo => SyncedUnixNow - this.LastSeen;

        /// <summary>Last Found Ago</summary>
        [IgnoreMember]
        [JsonIgnore]
        public double LastFoundAgo => SyncedUnixNow - this.LastFound;

        /// <summary>Last Killed Ago</summary>
        [IgnoreMember]
        [JsonIgnore]
        public double LastKilledAgo => SyncedUnixNow - this.LastKilled;

        /// <summary>Last Updated Ago</summary>
        [IgnoreMember]
        [JsonIgnore]
        public double LastUpdatedAgo => SyncedUnixNow - this.LastUpdated;

        /// <summary>Last Untouched Ago</summary>
        [IgnoreMember]
        [JsonIgnore]
        public double LastUntouchedAgo => SyncedUnixNow - this.LastUntouched;

        /// <summary>DPS Time</summary>
        [IgnoreMember]
        [JsonIgnore]
        public double DpsTime => this.LastSeen - this.LastUntouched;
        #endregion

        #region Getters
        public DateTimeOffset GetLastSeenDateTimeOffset() => DateTimeOffset.FromUnixTimeMilliseconds((long)this.LastSeen);
        public DateTimeOffset GetLastFoundDateTimeOffset() => DateTimeOffset.FromUnixTimeMilliseconds((long)this.LastFound);
        public DateTimeOffset GetLastKilledDateTimeOffset() => DateTimeOffset.FromUnixTimeMilliseconds((long)this.LastKilled);
        public DateTimeOffset GetLastUpdatedDateTimeOffset() => DateTimeOffset.FromUnixTimeMilliseconds((long)this.LastUpdated);
        public DateTimeOffset GetLastUntouchedDateTimeOffset() => DateTimeOffset.FromUnixTimeMilliseconds((long)this.LastUntouched);

        public DateTime GetLastSeen() => this.GetLastSeenDateTimeOffset().UtcDateTime;
        public DateTime GetLastFound() => this.GetLastFoundDateTimeOffset().UtcDateTime;
        public DateTime GetLastKilled() => this.GetLastKilledDateTimeOffset().UtcDateTime;
        public DateTime GetLastUpdated() => this.GetLastUpdatedDateTimeOffset().UtcDateTime;
        public DateTime GetLastUntouched() => this.GetLastUntouchedDateTimeOffset().UtcDateTime;
        #endregion

        #region Alive functions
        public bool IsAlive() => this.Relay.IsAlive();
        public bool IsDead() => this.Relay.IsDead();
        internal bool IsAliveInternal() => this.Relay.IsAliveInternal();
        internal bool IsDeadInternal() => this.Relay.IsDeadInternal();
        #endregion

        /// <summary>Relay State Status (based on <see cref="LastFound"/>, <see cref="LastSeen"/> and <see cref="LastKilled"/>)</summary>
        [JsonIgnore]
        [IgnoreMember]
        [SuppressMessage("Major Bug", "S1244", Justification = "Intended.")]
        public RelayStateStatus Status =>
            this.LastSeen == this.LastFound ? RelayStateStatus.Found :
            this.LastSeen == this.LastKilled ? RelayStateStatus.Killed :
            RelayStateStatus.Updated;

        /// <summary>Update the state with another state</summary>
        internal void UpdateWith(RelayState state, double now)
        {
            this.UpdateWithState(state);
            this.UpdateWithRelay(state.Relay, now);
        }

        /// <summary>Update the state with another state</summary>
        internal void UpdateWith(RelayState state) => this.UpdateWith(state, SyncedUnixNow);

        /// <summary>Update the state time information with another state</summary>
        internal void UpdateWithState(RelayState state)
        {
            this.Relay.UpdateWith(state.Relay);
            this.LastFound = this.LastFound.Max(state.LastFound);
            this.LastSeen = this.LastSeen.Max(state.LastSeen);
            this.LastKilled = this.LastKilled.Max(state.LastKilled);
            this.LastUntouched = this.LastUntouched.Max(state.LastUntouched);
        }

        /// <summary>Update the state time information with another state</summary>
        internal bool UpdateWithRelay(Relay relay, double now)
        {
            this.LastSeen = now;
            if (relay.IsAliveInternal())
            {
                if (!this.IsSameEntity(relay))
                {
                    this.LastFound = now;
                    this.LastUntouched = 0;
                }
                if (relay.IsUntouched()) this.LastUntouched = now;
            }
            else
            {
                this.LastKilled = now;
            }
            this.Relay.UpdateWith(relay);
            return true;
        }

        /// <summary>Update the state time information with another state</summary>
        internal bool UpdateWithRelay(Relay relay, double now, bool newEntity)
        {
            this.LastSeen = now;
            if (relay.IsAliveInternal())
            {
                if (newEntity)
                {
                    this.LastFound = now;
                    this.LastUntouched = 0;
                }
                if (relay.IsUntouched()) this.LastUntouched = now;
            }
            else
            {
                this.LastKilled = now;
            }
            this.Relay.UpdateWith(relay);
            return true;
        }

        /// <summary>Update the state time information with another state</summary>
        internal bool UpdateWithRelay(Relay relay) => this.UpdateWithRelay(relay, SyncedUnixNow);

        public bool IsValid() => this.Relay.IsValid();
        public bool IsSameEntity(Relay relay) => this.Relay.IsSameEntity(relay);
        public bool IsSameEntity(RelayState state) => this.IsSameEntity(state.Relay);
        public bool IsSimilarData(Relay relay) => this.IsSimilarData(relay, SyncedUnixNow);
        public bool IsSimilarData(RelayState state) => this.IsSimilarData(state.Relay, SyncedUnixNow);
        public bool IsSimilarData(Relay relay, double now) => now < this.LastSeen + TimeBeforeStale && this.Relay.IsSimilarData(relay, now);
        public bool IsSimilarData(RelayState state, double now) => this.IsSimilarData(state.Relay, now);

        public override string ToString()
        {
            return $"{this.Relay} (Found: {this.GetLastFound()} | Seen: {this.GetLastSeen()} | Killed: {this.GetLastKilled()})";
        }

        public override int GetHashCode() => this.Relay.GetHashCode();
        public bool Equals(RelayState? state) => Equals(this, state);
        public override bool Equals(object? obj) => ReferenceEquals(this, obj) || (obj is RelayState state && Equals(this, state));

        public static new bool Equals(object? left, object? right)
        {
            if (left is not RelayState leftState || right is not RelayState rightState) return object.Equals(left, right);
            return Equals(leftState, rightState);
        }

        public static bool Equals(RelayState? left, RelayState? right)
        {
            if (ReferenceEquals(left, right)) return true;
            if (left is null || right is null) return false;
            if (left.GetType() != right.GetType()) return false;
            return Relay.Equals(left.Relay, right.Relay);
        }

        public static bool operator ==(RelayState left, RelayState right) => Equals(left, right);
        public static bool operator !=(RelayState left, RelayState right) => !Equals(left, right);

        public string GetIndexKey(IndexType type) => this.Relay.GetIndexKey(type);
    }

    /// <summary>Represents a relay with state information. This class is managed by <see cref="RelayTracker{T}"/> and its not intended to be created manually.</summary>
    [MessagePackObject]
    [Serializable]
    public sealed class RelayState<T> : RelayState where T : Relay, IPlaceholderReplacementProvider
    {
        public RelayState(RelayState<T> s) : this(s.Relay)
        {
            this.LastSeen = s.LastSeen;
            this.LastFound = s.LastFound;
            this.LastKilled = s.LastKilled;
        }
        public RelayState(T r) : base(r) { }
        public RelayState(T r, double now) : base(r, now) { }

        /// <summary>Relay this state is for</summary>
        [Key(0)]
        public new T Relay
        {
            get => Unsafe.As<T>(base.Relay);
            set => base.Relay = value;
        }

        public bool IsSameEntity(T relay) => this.Relay.IsSameEntity(relay);
        public bool IsSameEntity(RelayState<T> state) => this.IsSameEntity(state.Relay);

        public bool TryGetValue(ReadOnlySpan<char> name, out ReadOnlySpan<char> value)
        {
            var result = name switch
            {
                "lastfound" => this.GetLastFoundDateTimeOffset().ToString(),
                "lastkilled" => this.GetLastKilledDateTimeOffset().ToString(),
                "lastseen" => this.GetLastSeenDateTimeOffset().ToString(),
                "lastuntouched" => this.GetLastUntouchedDateTimeOffset().ToString(),

                "sincefound" => $"{this.LastFoundAgo / 1000:F2}s",
                "sincekilled" => $"{this.LastKilledAgo / 1000:F2}s",
                "sinceseen" => $"{this.LastSeenAgo / 1000:F2}s",
                "sinceuntouched" => $"{this.LastUntouchedAgo / 1000:F2}s",

                _ => null
            };

            if (result is not null)
            {
                value = result;
                return true;
            }
            return this.Relay.TryGetValue(name, out value);
        }
    }

    public static class RelayStateExtensions
    {
        /// <summary>Get the Estimated Time To Death</summary>
        public static double GetEstimatedTimeToDeath(this RelayState<HuntRelay> state) => state.DpsTime * (100.0f / state.Relay.Progress);

        /// <summary>Get the Estimated Time To Completion</summary>
        public static double GetEstimatedTimeToCompletion(this RelayState<FateRelay> state) => state.DpsTime * (100.0f / state.Relay.Progress);
    }
}
