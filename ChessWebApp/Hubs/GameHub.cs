﻿using ChessWebApp.Models;
using Microsoft.AspNetCore.SignalR;
using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading.Tasks;

namespace ChessWebApp.Hubs
{
    public class GameHub : Hub
    {
        #region Private Variables
        private readonly ConcurrentDictionary<string, Player> players =
            new ConcurrentDictionary<string, Player>(StringComparer.OrdinalIgnoreCase);

        private readonly ConcurrentDictionary<string, Game> games =
            new ConcurrentDictionary<string, Game>(StringComparer.OrdinalIgnoreCase);

        private readonly ConcurrentQueue<Player> waitingPlayers =
            new ConcurrentQueue<Player>();
        #endregion

        #region private methods
        private Game GetGame(Player player, out Player opponent)
        {
            opponent = null;
            Game foundGame = games.Values.FirstOrDefault(g => g.Id == player.GameId);

            if (foundGame == null)
                return null;

            opponent = (player.Id == foundGame.Player1.Id) ? foundGame.Player2 : foundGame.Player1;
            return foundGame;
        }

        private void RemoveGame(string gameId)
        {
            Game foundGame;
            if (!this.games.TryRemove(gameId, out foundGame))
                throw new InvalidOperationException("Game not found.");

            players.TryRemove(foundGame.Player1.Id, out Player foundPlayer1);
            players.TryRemove(foundGame.Player2.Id, out Player foundPlayer2);
        }
        #endregion

        public async Task FindGame(string username)
        {
            Player joiningPlayer = new Player(username, Context.ConnectionId);
            players[joiningPlayer.Id] = joiningPlayer;
            await Clients.Caller.SendAsync("PlayerJoined", joiningPlayer);

            waitingPlayers.TryDequeue(out Player opponent);

            if (opponent == null)
            {
                waitingPlayers.Enqueue(joiningPlayer);
                await Clients.Caller.SendAsync("WaitingList");
            }
            else
            {
                joiningPlayer.Color = Color.Black;
                opponent.Color = Color.White;
                Game game = new Game(opponent, joiningPlayer);
                games[game.Id] = game;

                await Task.WhenAll(Groups.AddToGroupAsync(game.Player1.Id, groupName: game.Id), Groups.AddToGroupAsync(game.Player2.Id, groupName: game.Id), Clients.Group(game.Id).SendAsync("Start", game));
            }
        }

        public override Task OnConnectedAsync()
        {
            players.TryGetValue(Context.ConnectionId, out Player leavingPlayer);

            if (leavingPlayer != null)
            {
                Player opponent;
                Game ongoingGame = GetGame(leavingPlayer, out opponent);
                if (ongoingGame != null)
                {
                    Clients.Group(ongoingGame.Id).SendAsync("OpponentLeft");
                    RemoveGame(ongoingGame.Id);
                }
            }
            return base.OnConnectedAsync();
        }

        public async Task MoveSelected(string a, string b)
        {
            var player = players[Context.ConnectionId];
            if (!player.HasToMove)
                return;

            var game = GetGame(player, out Player opponent);
            var start = game.ChessBoard.GetSquareAtPosition(a);
            var end = game.ChessBoard.GetSquareAtPosition(b);
            game.MoveSelected(start, end);
            await Clients.All.SendAsync("MoveDone", game);
        }

        public async Task PieceSelected(int x, int y)
        {
            var player = players[Context.ConnectionId];
            if (!player.HasToMove)
                return;

            var game = GetGame(player, out Player opponent);
            var square = game.ChessBoard.GetSquareAtPosition(new Position(x, y));           
            game.ChessBoard.ClearBoardSelections();
            game.ChessBoard.CalculatePossibleMovesForPiece(square.Piece);
            square.IsSelected = true;
            await Clients.All.SendAsync("ShowPossibleMoves", game);
        }
    }
}
