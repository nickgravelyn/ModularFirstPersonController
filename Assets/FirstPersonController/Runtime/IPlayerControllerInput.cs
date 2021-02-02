﻿using UnityEngine;

namespace FirstPersonController
{
    public interface IPlayerControllerInput
    {
        Vector2 moveInput { get; }
        bool jump { get; }
        bool run { get; }
    }
}