﻿using System.Linq;
using System;

namespace PacMan
{
    public abstract class Ghost : SpriteBase, IGhost
    {
        private readonly Offset _initialPosition;
        private readonly IFrame _normalStateFrame;
        private readonly IFrame _deadStateFrame;
        private readonly IFrame _frightenedStateFrame;
        private readonly IGhostMovementStrategy _patrollingMode;
        private readonly IGhostMovementStrategy _chasingMode;
        private readonly IGhostMovementStrategy _frightenedMode;
        private readonly IGhostMovementStrategy _deadMode;
        private readonly int[] _changingModesTimeout = { 7, 20, 7, 20, 5, 20, 5, int.MaxValue };
        private readonly int _frightenedTimeout = 10;
        private IGhostMovementStrategy _currentMode;
        private DateTime _lastSwitchedTime;
        private DateTime _lastFrightenedTime;
        private int _timeoutsIndex = 0;

        protected Ghost(Offset position, Color color, Offset patrollingTarget, IGhostMovementStrategy chasingMode) :
            base(position, new GhostNormalFrame(color))
        {
            _initialPosition = position;
            _normalStateFrame = new GhostNormalFrame(color);
            _deadStateFrame = new GhostDeadFrame();
            _frightenedStateFrame = new GhostFrightenedFrame();

            _patrollingMode = new PatrolingMode(patrollingTarget);
            _chasingMode = chasingMode;
            _frightenedMode = new RandomTurnsMode();
            _deadMode = new DeadGhostMode();
            _currentMode = _patrollingMode;
            _lastSwitchedTime = DateTime.Now;
            _lastFrightenedTime = DateTime.Now;

            State = new(Offset.Default, Direction.Up);
        }

        public GhostMode Mode { get; private set; }

        public CharacterState State { get; private set; }

        public void Kill()
        {
            if (Mode != GhostMode.Dead)
            {
                _currentMode = _deadMode;
                Mode = GhostMode.Dead;
                SetCurrentFrame(_deadStateFrame);
            }
        }

        public void Ressurect()
        {
            if (Mode == GhostMode.Dead)
            {
                _currentMode = _patrollingMode;
                Mode = GhostMode.Patroling;
                SetCurrentFrame(_normalStateFrame);
            }
        }

        public void Frighten()
        {
            if (Mode != GhostMode.Dead)
            {
                _lastFrightenedTime = DateTime.Now;
                _currentMode = _frightenedMode;
                State = State with { Direction = State.Direction.ToOpposite() };
                Mode = GhostMode.Frightened;
                SetCurrentFrame(_frightenedStateFrame);
            }
        }

        public void Comfort()
        {
            if (Mode == GhostMode.Frightened)
            {
                _currentMode = _timeoutsIndex % 2 == 1 ? _chasingMode : _patrollingMode;
                Mode = GhostMode.Chasing;
                SetCurrentFrame(_normalStateFrame);
            }
        }

        public void Move(SelfMovementContext context)
        {
            // get next target based on ghost mode and corresponding movement strategy
            var currentVertex = Position.ToTile();
            var currentTile = context.Map[currentVertex.Y, currentVertex.X];

            // check if ghost needs to change the direction
            if (Position.Equals(currentTile.Position))
            {
                CheckTimeoutBeforeBeingComforted(DateTime.Now);
                CheckTimeoutBeforeChangingMode(DateTime.Now);

                var movementContext = new GhostMovementContext(context.EventSink, context.Map, this);
                var target = _currentMode.Execute(movementContext);
                var ghostPosition = Position;
                var ghostDirection = State.Direction;

                var graph = context.Map;
                var neighbors = graph.GetNeighbors(currentVertex)
                    .Where(neighbor => !neighbor.IsWall)
                    .ToList();

                var allowedDirections = neighbors
                    .Select(neighbor => ghostPosition.ToDirection(neighbor.Position))
                    .Where(direction => direction != ghostDirection.ToOpposite())
                    .Where(direction => direction != currentTile.Restriction)
                    .ToList();

                if (allowedDirections.Count == 1)
                {
                    // there is only one way to go/turn - go/turn that way
                    State = State with { Direction = allowedDirections.Single() };
                }
                else if (allowedDirections.Count == 0)
                {
                    // there were no ways to go/turn, choosing the direction closest to target
                    var targetDirection = neighbors
                        .Select(neighbor => neighbor.Position)
                        .OrderBy(neighbor => neighbor.EuclideanDistance(target))
                        .First();

                    State = State with { Direction = ghostPosition.ToDirection(targetDirection) };
                }
                else if (neighbors.Count >= 3)
                {
                    // current tile is the tile in which we need to decide where to go/turn
                    // TODO: check the direction accroding to the priorities below
                    // Direction.Up, Direction.Left, Direction.Down
                    var targetDirection = allowedDirections
                        .Select(direction => Position.Shift(direction.ToOffset()))
                        .OrderBy(neighbor => neighbor.EuclideanDistance(target))
                        .First();

                    State = State with { Direction = ghostPosition.ToDirection(targetDirection) };
                }
            }

            var nextPosition = Position.Shift(State.Direction.ToOffset());
            var afterVertex = nextPosition.ToTile();
            var afterTile = context.Map[afterVertex.Y, afterVertex.X];
            Position = afterTile.IsWall ? Position : nextPosition;
        }

        public void Reset()
        {
            _currentMode = _patrollingMode;
            State = new (Offset.Default, Direction.Right);
            Position = _initialPosition;
            Mode = GhostMode.Patroling;
            SetCurrentFrame(_normalStateFrame);
        }

        public void Effect(FoodContext context)
        {
            if (context.Eatable is IPacMan
                && Mode == GhostMode.Frightened
                && Mode != GhostMode.Dead)
            {
                Kill();
                context.GameState.UpScore(200 * context.GameState.Multiplier);
                context.GameState.UpMultiplier();
                context.EventSink.Publish(new GhostEaten());
            }
        }

        private void CheckTimeoutBeforeBeingComforted(DateTime currentTime)
        {
            var elapsedSeconds = currentTime.Subtract(_lastFrightenedTime).TotalSeconds;

            if (elapsedSeconds >= _frightenedTimeout && Mode == GhostMode.Frightened)
            {
                Comfort();
            }
        }

        private void CheckTimeoutBeforeChangingMode(DateTime currentTime)
        {
            var elapsedSeconds = currentTime.Subtract(_lastSwitchedTime).TotalSeconds;
            var expectedSeconds = _changingModesTimeout[_timeoutsIndex];

            if (elapsedSeconds >= expectedSeconds && Mode != GhostMode.Frightened && Mode != GhostMode.Dead)
            {
                _timeoutsIndex = (_timeoutsIndex + 1) % _changingModesTimeout.Length;
                _currentMode = _timeoutsIndex % 2 == 1 ? _chasingMode : _patrollingMode;
                _lastSwitchedTime = currentTime;
            }
        }

        private sealed class PatrolingMode : IGhostMovementStrategy
        {
            private readonly Offset _target;

            public PatrolingMode(Offset target) => _target = target;

            public Offset Execute(GhostMovementContext context) => _target;
        }

        private sealed class DeadGhostMode : IGhostMovementStrategy
        {
            private readonly Offset _respawnTarget = new Offset(9, 9).Extend(Tile.SIZE);

            public Offset Execute(GhostMovementContext context) => _respawnTarget;
        }

        private sealed class RandomTurnsMode : IGhostMovementStrategy
        {
            private readonly Random _randomGenerator = new Random();

            public Offset Execute(GhostMovementContext context)
            {
                var currentVertex = context.Ghost.Position.ToTile();
                var currentTile = context.Map[currentVertex.Y, currentVertex.X];
                var ghostPosition = context.Ghost.Position;
                var ghostDirection = context.Ghost.State.Direction;

                var neighbors = context.Map.GetNeighbors(currentVertex)
                    .Where(neighbor => !neighbor.IsWall)
                    .ToList();

                var allowedDirections = neighbors
                    .Select(neighbor => ghostPosition.ToDirection(neighbor.Position))
                    .Where(direction => direction != ghostDirection.ToOpposite())
                    .Where(direction => direction != currentTile.Restriction)
                    .ToList();

                int randomIndex = _randomGenerator.Next(allowedDirections.Count);
                var nextDirection = allowedDirections.Any() ? allowedDirections[randomIndex] : ghostDirection;
                var ghostTarget = ghostPosition.Shift(nextDirection.ToOffset());
                return ghostTarget;
            }
        }
    }
}
