using System.Collections.Generic;
using UnityEngine;

namespace Nekuzaky.Hindsight.Demo
{
    public sealed class DemoBootstrap : MonoBehaviour
    {
        #region Inspector

        [SerializeField] private int _seed = 7;
        [SerializeField] private float _spawnInterval = 0.45f;
        [SerializeField] private int _maxBalls = 24;
        [SerializeField] private int _cometEvery = 7;
        [SerializeField] private float _cometSpeed = 140f;

        #endregion


        #region Private and Protected

        private const float HalfWidth = 8f;
        private const float FloorY = -3.6f;
        private const float FloorThickness = 0.2f;
        private const float SpawnHeight = 4.6f;
        private const float BallRadius = 0.32f;
        private const int SpriteSize = 64;

        private static readonly Color[] BallColors =
        {
            new(0.55f, 0.49f, 1f), new(0.22f, 0.74f, 0.97f), new(0.24f, 0.86f, 0.52f),
            new(1f, 0.69f, 0.13f), new(0.96f, 0.45f, 0.71f), new(0.6f, 0.9f, 0.95f)
        };

        private readonly Queue<GameObject> _balls = new();
        private System.Random _random;
        private Sprite _circle;
        private Sprite _square;
        private Material _material;
        private Transform _paddle;
        private float _nextSpawn;
        private int _spawned;

        #endregion


        #region Unity Callbacks

        private void Awake()
        {
            _random = new System.Random(_seed);
            _material = CreateMaterial();
            _circle = CreateSprite(circle: true);
            _square = CreateSprite(circle: false);
            SetUpCamera();
            BuildWorld();
        }

        private void Update()
        {
            _paddle.Rotate(0f, 0f, 70f * Time.deltaTime);
            if (Time.time < _nextSpawn)
                return;

            _nextSpawn = Time.time + _spawnInterval;
            Spawn();
        }

        #endregion


        #region Tools and Utilities

        private void Spawn()
        {
            _spawned++;
            var comet = _cometEvery > 0 && _spawned % _cometEvery == 0;
            var side = _random.NextDouble() < 0.5d ? -1f : 1f;
            var x = comet
                ? side * Mathf.Lerp(3f, HalfWidth - 1.2f, (float)_random.NextDouble())
                : Mathf.Lerp(-HalfWidth + 1.5f, HalfWidth - 1.5f, (float)_random.NextDouble());
            var y = comet ? CometSpawnHeight() : SpawnHeight;
            var ball = CreateBody($"{(comet ? "Comet" : "Ball")} {_spawned}", _circle, new Vector2(x, y), Vector2.one * BallRadius * 2f,
                comet ? new Color(1f, 0.36f, 0.36f) : BallColors[_spawned % BallColors.Length]);
            ball.AddComponent<CircleCollider2D>().radius = 0.5f;

            var body = ball.AddComponent<Rigidbody2D>();
            body.collisionDetectionMode = CollisionDetectionMode2D.Discrete;
            var demo = ball.AddComponent<DemoBall>();
            demo.Number = _spawned;
            demo.IsComet = comet;

            if (comet)
            {
                body.linearVelocity = new Vector2(0f, -_cometSpeed);
                Hindsight.Bookmark($"Comet #{_spawned} launched");
            }
            else
            {
                body.linearVelocity = new Vector2(Mathf.Lerp(-3f, 3f, (float)_random.NextDouble()), 0f);
            }

            _balls.Enqueue(ball);
            while (_balls.Count > _maxBalls)
            {
                var oldest = _balls.Dequeue();
                if (oldest != null)
                    Destroy(oldest);
            }
        }

        private float CometSpawnHeight()
        {
            var dt = Time.fixedDeltaTime;
            var step = _cometSpeed * dt;
            var band = FloorThickness + BallRadius * 2f;
            if (step <= band)
                return SpawnHeight;

            var target = FloorY + band * 0.5f + (step - band) * 0.5f;
            var steps = Mathf.FloorToInt((SpawnHeight - target) / step);
            var gravity = Mathf.Abs(Physics2D.gravity.y) * dt * dt * steps * (steps + 1) * 0.5f;
            return target + steps * step + gravity;
        }

        private void BuildWorld()
        {
            var wall = new Color(0.2f, 0.22f, 0.28f);
            Static("Floor", new Vector2(0f, FloorY), new Vector2(HalfWidth * 2f, FloorThickness), wall);
            Static("Left Wall", new Vector2(-HalfWidth, 0.6f), new Vector2(0.3f, 8.6f), wall);
            Static("Right Wall", new Vector2(HalfWidth, 0.6f), new Vector2(0.3f, 8.6f), wall);

            var paddle = CreateBody("Paddle", _square, new Vector2(0f, -0.6f), new Vector2(4.2f, 0.28f), new Color(0.55f, 0.49f, 1f));
            paddle.AddComponent<BoxCollider2D>();
            paddle.AddComponent<Rigidbody2D>().bodyType = RigidbodyType2D.Kinematic;
            _paddle = paddle.transform;
        }

        private void Static(string name, Vector2 position, Vector2 size, Color color)
        {
            var piece = CreateBody(name, _square, position, size, color);
            piece.AddComponent<BoxCollider2D>();
        }

        private GameObject CreateBody(string name, Sprite sprite, Vector2 position, Vector2 size, Color color)
        {
            var body = new GameObject(name);
            body.transform.SetParent(transform, false);
            body.transform.position = position;
            body.transform.localScale = new Vector3(size.x, size.y, 1f);
            var renderer = body.AddComponent<SpriteRenderer>();
            renderer.sprite = sprite;
            renderer.color = color;
            if (_material != null)
                renderer.sharedMaterial = _material;
            return body;
        }

        private static void SetUpCamera()
        {
            var camera = Camera.main;
            if (camera == null)
                return;
            camera.orthographic = true;
            camera.orthographicSize = 5f;
            camera.transform.position = new Vector3(0f, 0.4f, -10f);
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.07f, 0.075f, 0.094f);
        }

        private static Material CreateMaterial()
        {
            string[] candidates = { "Universal Render Pipeline/2D/Sprite-Unlit-Default", "Universal Render Pipeline/Unlit", "Sprites/Default" };
            foreach (var name in candidates)
            {
                var shader = Shader.Find(name);
                if (shader != null)
                    return new Material(shader);
            }
            return null;
        }

        private static Sprite CreateSprite(bool circle)
        {
            var texture = new Texture2D(SpriteSize, SpriteSize, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear };
            var pixels = new Color32[SpriteSize * SpriteSize];
            var center = (SpriteSize - 1) * 0.5f;
            for (var y = 0; y < SpriteSize; y++)
            {
                for (var x = 0; x < SpriteSize; x++)
                {
                    var distance = Mathf.Sqrt((x - center) * (x - center) + (y - center) * (y - center));
                    var alpha = circle ? Mathf.Clamp01(center - distance + 0.5f) : 1f;
                    pixels[y * SpriteSize + x] = new Color32(255, 255, 255, (byte)(alpha * 255f));
                }
            }
            texture.SetPixels32(pixels);
            texture.Apply();
            return Sprite.Create(texture, new Rect(0, 0, SpriteSize, SpriteSize), new Vector2(0.5f, 0.5f), SpriteSize);
        }

        #endregion
    }
}
