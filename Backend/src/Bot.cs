using System;

namespace Backend
{
    public class Bot
    {
        public Guid Id { get; init; } = Guid.NewGuid();
        public string Name { get; set; } = string.Empty;
        public float X { get; set; }
        public float Y { get; set; }
        public float Z { get; set; }
        public DateTime LastUpdate { get; set; } = DateTime.UtcNow;
        public bool IsActive { get; set; } = true;
        public BotBehavior Behavior { get; set; } = BotBehavior.Idle;
        public float Speed { get; set; } = 3f;
        public Guid? TargetId { get; set; } = null;
        
        // Random movement state
        public Vector3? MoveDirection { get; set; } = null;
        public DateTime LastDirectionChange { get; set; } = DateTime.UtcNow;
        
        // Movement bounds (to keep bots within reasonable area)
        public float MinX { get; set; } = -25f;
        public float MaxX { get; set; } = 25f;
        public float MinZ { get; set; } = -25f;
        public float MaxZ { get; set; } = 25f;
        
        private static readonly Random Random = new Random();
        
        public void Update(float deltaTime)
        {
            if (!IsActive) return;
            
            switch (Behavior)
            {
                case BotBehavior.Random:
                    UpdateRandomMovement(deltaTime);
                    break;
                // Other behaviors can be added here
            }
            
            LastUpdate = DateTime.UtcNow;
        }
        
        private void UpdateRandomMovement(float deltaTime)
        {
            // Change direction every 1-3 seconds (more frequent direction changes)
            var timeSinceDirectionChange = DateTime.UtcNow - LastDirectionChange;
            if (MoveDirection == null || timeSinceDirectionChange.TotalSeconds > Random.Next(1, 4))
            {
                // Generate random direction
                var angle = Random.NextSingle() * 2 * Math.PI;
                MoveDirection = new Vector3(
                    (float)Math.Cos(angle),
                    0,
                    (float)Math.Sin(angle)
                );
                LastDirectionChange = DateTime.UtcNow;
            }
            
            // Move in current direction
            if (MoveDirection.HasValue)
            {
                var moveVector = MoveDirection.Value;
                var newX = X + moveVector.X * Speed * deltaTime;
                var newZ = Z + moveVector.Z * Speed * deltaTime;
                
                // Check bounds and bounce if necessary
                if (newX < MinX || newX > MaxX)
                {
                    moveVector.X = -moveVector.X;
                    MoveDirection = moveVector;
                    newX = X + moveVector.X * Speed * deltaTime;
                }
                
                if (newZ < MinZ || newZ > MaxZ)
                {
                    moveVector.Z = -moveVector.Z;
                    MoveDirection = moveVector;
                    newZ = Z + moveVector.Z * Speed * deltaTime;
                }
                
                X = newX;
                Z = newZ;
            }
        }
        
        public PlayerState ToPlayerState()
        {
            return new PlayerState
            {
                Id = Id,
                Name = Name,
                X = X,
                Y = Y,
                Z = Z,
                LastUpdate = LastUpdate,
                IsBot = true,
                BotBehavior = Behavior,
                BotSpeed = Speed
            };
        }
    }
}
