﻿using UnityEngine;

namespace FirstPersonController
{
    public interface IRunIntent : IIntent
    {
        bool wantsToStartRunning { get; }
        bool wantsToStopRunning { get; }
        Vector2 moveDirection { get; }
    }
}