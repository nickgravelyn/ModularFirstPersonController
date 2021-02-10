﻿using System;
using UnityEngine;

namespace FirstPersonController
{
    [CreateAssetMenu(menuName = "First Person Controller/Abilities/Walk")]
    public sealed class Walk : PlayerAbility
    {
        [SerializeField]
        private PlayerSpeed _speed = new PlayerSpeed(2f, 1f, 0.95f);

        public override bool isBlocking => true;

        public override bool canActivate => controller.canStandUp;

        public override void OnActivate()
        {
            controller.ResetHeight();
        }

        public override void FixedUpdate()
        {
            controller.ApplyUserInputMovement(_speed);
        }
    }
}