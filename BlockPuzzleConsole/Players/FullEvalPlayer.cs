using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using ConsoleApp1.Utils;
using PuzzleBlock.Utils;

namespace PuzzleBlock.Players
{
    interface IFullEvalPlayer
    {
        GamePath SelectBestPath(List<GamePath> paths);
        void GatherStepStats(Candidate candidate, GamePath gamePath, Board board, Board newBoard);
        void GatherPathStats(GamePath gamePath, Board board);
    }

    abstract class FullEvalPlayerBase : IPlayer, IFullEvalPlayer
    {
        private IList<Candidate> CalcMoves = new List<Candidate>();

        private int possibleMoves;

        public void MakeAMove(out int shapeId, out string placement, Board board, IDictionary<int, Shape> shapes,
            IGameDrawer renderer)
        {
            shapeId = 0;
            placement = "";

            if (CalcMoves.Count == 0)
            {
                possibleMoves = 0;
                Stopwatch stopWatch = new Stopwatch();
                stopWatch.Start();

                var gamePaths = new List<GamePath>();

                renderer.ShowUpdateMessageStart("Calculating possible moves... ");
                InnerMakeAMove(board, shapes, gamePaths, null, renderer);

                var best = SelectBestPath(gamePaths);

                foreach (var cand in best.Moves)
                    CalcMoves.Add(cand);

                Console.WriteLine();
                stopWatch.Stop();
                if (possibleMoves > 0)
                    renderer.ShowMessage("Throughput: " +
                                         (float)possibleMoves / (float)(stopWatch.ElapsedMilliseconds / 1000) + "/sec");
            }
            shapeId = CalcMoves[0].ShapeId;
            placement = CalcMoves[0].Placement;
            CalcMoves.RemoveAt(0);
        }

        private void InnerMakeAMove(Board board, IDictionary<int, Shape> shapes,
            IList<GamePath> gamePaths, GamePath startGamePath, IGameDrawer renderer)
        {
            if (shapes.Count == 0)
            {
                gamePaths.Add(startGamePath);
                GatherPathStats(startGamePath, board);
                return;
            }

            var placed = false;
            foreach (var shape in shapes)
            {
                for (int x = 0; x < 8; x++)
                {
                    for (int y = 0; y < 8; y++)
                    {
                        var curPlacement = "" + (char) (97 + x) + (char) (49 + y);
                        var newBoard = new Board(board);
                        if (newBoard.TryPlace(shape.Value, curPlacement))
                        {
                            possibleMoves++;
                            placed = true;
                            var gamePath = new GamePath(startGamePath);

                            var candidate = new Candidate()
                            {
                                ShapeId = shape.Key,
                                Placement = curPlacement,
                            };
                            gamePath.Moves.Add(candidate);

                            GatherStepStats(candidate, gamePath, board, newBoard);

                            var newShapes = new Dictionary<int, Shape>();
                            foreach (var sh in shapes)
                                if (sh.Key != shape.Key)
                                    newShapes.Add(sh.Key, sh.Value);

                            InnerMakeAMove(newBoard, newShapes, gamePaths, gamePath, renderer);
                        }
                    }
                }
            }
            renderer.ShowUpdateMessage("[" + possibleMoves.ToString("N0") + "]");
            if (!placed)
                gamePaths.Add(startGamePath);
        }

        public abstract GamePath SelectBestPath(List<GamePath> paths);
        public abstract void GatherStepStats(Candidate candidate, GamePath gamePath, Board board, Board newBoard);
        public abstract void GatherPathStats(GamePath gamePath, Board board);
    }

    class FullEvalPlayer : FullEvalPlayerBase
    {
        private IList<IDictionary<int, Shape>> AllNextShaps = null;
        public override GamePath SelectBestPath(List<GamePath> paths)
        {            
            var topScorePath = from x in paths orderby x.Moves.Count descending, x.ScoreGain descending, x.PlacementScore descending select x;
            var topScorePathByNextDraw3 = from x in paths orderby x.Moves.Count descending, x.PlacementScore descending, x.ScoreGain descending select x;

            var topPath = topScorePath.First();
            var topPathByNextDraw3 = topScorePathByNextDraw3.First();

            if (topPath.CellCount < 30 || topPath.MaxArea >= 0.35f)
                return topPath;
            if (topPathByNextDraw3.PlacementScore == 0)
                return topPath;

                return topPathByNextDraw3;
        }

        public override void GatherStepStats(Candidate candidate, GamePath gamePath, Board board, Board newBoard)
        {
            var cellsGain = newBoard.CellCount() - board.CellCount();
            var scoreGain = newBoard.Score - board.Score;
            candidate.CellsGain = cellsGain;
            candidate.ScoreGain = scoreGain;

            gamePath.CellsGain += cellsGain;
            gamePath.ScoreGain += scoreGain;
            gamePath.CellCount = newBoard.CellCount();
        }

        public override void GatherPathStats(GamePath gamePath, Board board)
        {
            gamePath.MaxArea = (float)MaxArea.MaximalRectangle(board.Cells) / 64;
            gamePath.FragScore = Fragmentation.GetFragmentationScore(board.Cells);

            if (gamePath.CellCount < 30 || gamePath.MaxArea >= 0.35f)
                gamePath.PlacementScore = BoardScore(board);
            else
                gamePath.PlacementScore = BoardScoreByNextDraw3(board);
        }

        private float BoardScore(Board newBoard)
        {
            float score = 1;

            for (int x = 0; x < 8; x++)
            {
                for (int y = 0; y < 8; y++)
                {
                    if (!newBoard.Cells[x][y])
                        continue;

                    float mult1 = 0;
                    float mult2 = 0;

                    if (y == 0 || y == 7)
                        mult1 = 1;
                    if (y == 1 || y == 6)
                        mult1 = 0.75F;
                    if (y == 2 || y == 5)
                        mult1 = 0.5F;
                    if (y == 3 || y == 4)
                        mult1 = 0.25F;

                    if (x == 0 || x == 7)
                        mult2 = 1;
                    if (x == 1 || x == 6)
                        mult2 = 0.75F;
                    if (x == 2 || x == 5)
                        mult2 = 0.5F;
                    if (x == 3 || x == 4)
                        mult2 = 0.25F;

                    score *= mult1 * mult2;
                }
            }
            return score;
        }

        private float BoardScoreByNextDraw3(Board newBoard)
        {
            if(AllNextShaps == null)
                AllNextShaps = CreateAllNextShaps();

            int max = AllNextShaps.Count;

            foreach (var shapes in AllNextShaps)
            {
                foreach (Shape shape in shapes.Values)
                {
                    if (shape.ShapeType == Shape.Type.LargeSquare)
                        max += 3;
                    if (shape.ShapeType == Shape.Type.FiveLiner)
                        max += 2;
                }
            }

            int count = 0;
            foreach (var shapes in AllNextShaps)
            {
                if (CanPlaceAllShapes(newBoard, shapes))
                {
                    count++;
                    foreach (Shape shape in shapes.Values)
                    {
                        if (shape.ShapeType == Shape.Type.LargeSquare)
                            count += 3;
                        if (shape.ShapeType == Shape.Type.FiveLiner)
                            count += 2;
                    }
                }
            }

            return (float) Math.Round((double)count / max, 3);
        }

        private bool CanPlaceAllShapes(Board newBoard, IDictionary<int, Shape> shapes)
        {
            var gamePaths = new List<GamePath>();
            TryMakeNextDraw3(newBoard, shapes, gamePaths, null);
            return gamePaths.Count > 0;
        }

        private void TryMakeNextDraw3(Board board, IDictionary<int, Shape> shapes,
            IList<GamePath> gamePaths, GamePath startGamePath)
        {
            if (shapes.Count == 0)
            {
                gamePaths.Add(startGamePath);
                return;
            }

            foreach (var shape in shapes)
            {
                for (int x = 0; x < 8 && gamePaths.Count == 0; x++)
                {
                    for (int y = 0; y < 8 && gamePaths.Count == 0; y++)
                    {
                        var curPlacement = "" + (char)(97 + x) + (char)(49 + y);
                        var newBoard = new Board(board);
                        if (newBoard.TryPlace(shape.Value, curPlacement))
                        {
                            var gamePath = new GamePath(startGamePath);

                            var candidate = new Candidate()
                            {
                                ShapeId = shape.Key,
                                Placement = curPlacement,
                            };
                            gamePath.Moves.Add(candidate);
                            

                            var newShapes = new Dictionary<int, Shape>();
                            foreach (var sh in shapes)
                                if (sh.Key != shape.Key)
                                    newShapes.Add(sh.Key, sh.Value);

                            TryMakeNextDraw3(newBoard, newShapes, gamePaths, gamePath);
                        }
                    }
                }
            }
        }

        /// <summary>
        ///     Create all combinations of next 3 shapes using the "angry" shapes
        /// </summary>
        /// <returns></returns>
        private IList<IDictionary<int, Shape>> CreateAllNextShaps()
        {
            IList<IDictionary<int, Shape>> AllNextShapes = new List<IDictionary<int, Shape>>();
            IDictionary<int, Shape> Allshapes = CreateAllShapes();
            int max = Allshapes.Count;

            for (int i = 0; i < max; i++)
            {
                for (int j = i+1; j < max; j++)
                {
                    for (int k = j+1; k < max; k++)
                    {
                        AllNextShapes.Add(new Dictionary<int, Shape>() { {1, Allshapes[i] }, {2, Allshapes[j] }, {3, Allshapes[k] } });
                    }
                }
            }

            return AllNextShapes;
        }

        /// <summary>
        ///     Create all "angry" shapes meaning the shapes that are most likeley to cause problems
        /// </summary>
        /// <returns></returns>
        private IDictionary<int, Shape> CreateAllShapes()
        {
            IDictionary<int, Shape> Allshapes = new Dictionary<int, Shape>();

            var shapeVals = Enum.GetValues(typeof(Shape.Type));
            var shapeOrientations = Enum.GetValues(typeof(Shape.ShapeOrientation));
            int key = 0;
            foreach (Shape.Type type in shapeVals)
            {
                if (type == Shape.Type.Singler || type == Shape.Type.ThreeLiner || type == Shape.Type.FourLiner ||
                    type == Shape.Type.SmallL || type == Shape.Type.SmallSquare || type == Shape.Type.LongTail ||
                    type == Shape.Type.LargeL)
                    continue;
                foreach (Shape.ShapeOrientation orientation in shapeOrientations)
                {
                    if (((type == Shape.Type.FiveLiner) && (orientation == Shape.ShapeOrientation.E || orientation == Shape.ShapeOrientation.N)) ||
                        (type == Shape.Type.LargeSquare && orientation != Shape.ShapeOrientation.E))
                        continue;
                    Allshapes.Add(key, new Shape(type, orientation));
                    key++;
                }
            }

            return Allshapes;
        }

        //private float BoardScoreByNextStep(Board newBoard)
        //{
        //    var score = 1f;
        //    var FiveLinerE = new Shape(Shape.Type.FiveLiner, Shape.ShapeOrientation.E);
        //    var FiveLinerN = new Shape(Shape.Type.FiveLiner, Shape.ShapeOrientation.N);
        //    var FourLinerE = new Shape(Shape.Type.FourLiner, Shape.ShapeOrientation.E);
        //    var FourLinerN = new Shape(Shape.Type.FourLiner, Shape.ShapeOrientation.N);
        //    var LargeSquare = new Shape(Shape.Type.LargeSquare, Shape.ShapeOrientation.N);

        //    if (!newBoard.CanFitAnywhere(FiveLinerE))
        //        score *= 0.9f;
        //    if (!newBoard.CanFitAnywhere(FiveLinerN))
        //        score *= 0.9f;
        //    if (!newBoard.CanFitAnywhere(FourLinerE))
        //        score *= 0.9f;
        //    if (!newBoard.CanFitAnywhere(FourLinerN))
        //        score *= 0.9f;
        //    if (!newBoard.CanFitAnywhere(LargeSquare))
        //        score *= 0.8f;

        //    return score;
        //}

        //private float BoardScoreWithWeight(Board newBoard)
        //{
        //    float score = 1;

        //    var BestX = FindMostWeightX(newBoard);
        //    var BestY = FindMostWeightY(newBoard);

        //    for (int x = 0; x < 8; x++)
        //    {
        //        for (int y = 0; y < 8; y++)
        //        {
        //            if (!newBoard.Cells[x][y])
        //                continue;

        //            var offFromBestY = Math.Abs(y - BestY);
        //            var offFromBestX = Math.Abs(x - BestX);

        //            float mult1 = 0;
        //            float mult2 = 0;

        //            if (offFromBestY == 0)
        //                mult1 = 1;
        //            if (offFromBestY == 1)
        //                mult1 = 0.875F;
        //            if (offFromBestY == 2)
        //                mult1 = 0.75F;
        //            if (offFromBestY == 3)
        //                mult1 = 0.625F;
        //            if (offFromBestY == 4)
        //                mult1 = 0.5F;
        //            if (offFromBestY == 5)
        //                mult1 = 0.375F;
        //            if (offFromBestY == 6)
        //                mult1 = 0.25F;
        //            if (offFromBestY == 7)
        //                mult1 = 0.25F;

        //            if (offFromBestX == 0)
        //                mult2 = 1;
        //            if (offFromBestX == 1)
        //                mult2 = 0.875F;
        //            if (offFromBestX == 2)
        //                mult2 = 0.75F;
        //            if (offFromBestX == 3)
        //                mult2 = 0.625F;
        //            if (offFromBestX == 4)
        //                mult2 = 0.5F;
        //            if (offFromBestX == 5)
        //                mult2 = 0.375F;
        //            if (offFromBestX == 6)
        //                mult2 = 0.25F;
        //            if (offFromBestX == 7)
        //                mult2 = 0.25F;

        //            score *= mult1 * mult2;
        //        }
        //    }
        //    return score;
        //}

        //private int FindMostWeightY(Board newBoard)
        //{
        //    int bestY = 0;
        //    int bestSum = 0;

        //    int tempSum = 0;
        //    for (int x = 0; x < 8; x++)
        //    {
        //        for (int y = 0; y < 4; y++)
        //        {
        //            if (!newBoard.Cells[x][y])
        //                continue;

        //            tempSum++;
        //        }
        //    }
        //    bestSum = tempSum;

        //    tempSum = 0;
        //    for (int x = 0; x < 8; x++)
        //    {
        //        for (int y = 4; y < 8; y++)
        //        {
        //            if (!newBoard.Cells[x][y])
        //                continue;

        //            tempSum++;
        //        }
        //    }

        //    bestY = bestSum >= tempSum ? 0 : 7;
        //    return bestY;
        //}

        //private int FindMostWeightX(Board newBoard)
        //{
        //    int bestX = 0;
        //    int bestSum = 0;

        //    int tempSum = 0;
        //    for (int x = 0; x < 4; x++)
        //    {
        //        for (int y = 0; y < 8; y++)
        //        {
        //            if (!newBoard.Cells[x][y])
        //                continue;

        //            tempSum++;
        //        }
        //    }
        //    bestSum = tempSum;

        //    tempSum = 0;
        //    for (int x = 4; x < 8; x++)
        //    {
        //        for (int y = 0; y < 8; y++)
        //        {
        //            if (!newBoard.Cells[x][y])
        //                continue;

        //            tempSum++;
        //        }
        //    }

        //    bestX = bestSum >= tempSum ? 0 : 7;
        //    return bestX;
        //}
    }

    class GamePath
    {
        public int CellsGain { get; set; }
        public float CellGainNorm => ((float)(1 - ((float)CellsGain - (-43)) / ((float)27 - (-43))));
        public int ScoreGain { get; set; }
        public float ScoreGainNorm => ((float)(ScoreGain - 0)) / (127);
        public int CellCount { get; set; }
        public float PlacementScore { get; set; }
        public float MaxArea { get; set; }
        public float FragScore { get; set; }

        public IList<Candidate> Moves { get; set; }

        public GamePath()
        {
            Moves = new List<Candidate>();
            PlacementScore = 1;
        }

        public GamePath(GamePath source) : this()
        {
            if (source != null)
            {
                foreach (var candidate in source.Moves)
                    Moves.Add(candidate);

                CellsGain = source.CellsGain;
                ScoreGain = source.ScoreGain;
                CellCount = source.CellCount;
                PlacementScore = source.PlacementScore;
            }
        }
    }
}