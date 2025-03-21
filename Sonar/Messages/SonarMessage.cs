﻿using MessagePack;
using System;

namespace Sonar.Messages
{
    /// <summary>Sonar message meant to be shown in chat or popup</summary>
    [MessagePackObject]
    [Serializable]
    public sealed class SonarMessage : ISonarMessage
    {
        /// <summary>Message</summary>
        [Key(0)]
        public required string Message { get; init; }
    }
}
