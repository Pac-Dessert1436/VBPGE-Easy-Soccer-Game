Option Strict On
Option Infer On
Imports VbPixelGameEngine

Public NotInheritable Class Program
    Inherits PixelGameEngine

    ' Defining assets
    Private ssSoccerBall As SpriteSheet
    Private ssSoccerPlayer As SpriteSheet
    Private ReadOnly sprSoccerField As New Sprite("Assets/soccer_field.png")
    Private ReadOnly sprGoalLeft As New Sprite("Assets/goal_left.png")
    Private ReadOnly sprGoalRight As New Sprite("Assets/goal_right.png")
    Private ReadOnly bgmMainTheme As New SoundPlayer("Assets/main_theme.mp3")
    Private ReadOnly sndKick As New SoundPlayer("Assets/kick.mp3")
    Private ReadOnly sndGoal As New SoundPlayer("Assets/goal.mp3")
    Private ReadOnly sndWhistle As New SoundPlayer("Assets/whistle.mp3")

    ' Core variables in the game
    Private m_soccer As Actor
    Private m_gameState As GameState
    Private ReadOnly m_bluePlayers(10) As Actor ' Blue team has 11 players
    Private ReadOnly m_redPlayers(10) As Actor ' Red team has 11 players
    Private m_blueScore As Integer
    Private m_redScore As Integer
    Private m_blueControlledIdx As Integer ' Blue team's current controlled player index
    Private m_redControlledIdx As Integer ' Red team's current controlled player index
    Private m_aiControllers As List(Of AIController)
    Private m_blueGoalPosition As Vf2d ' Blue team's attack target (red team's goal)
    Private m_redGoalPosition As Vf2d ' Red team's attack target (blue team's goal)
    Private m_kickBackTimer As Single ' Delayed action timer (corner kick/goal kick)

    Public Sub New()
        AppName = "VBPGE Easy Soccer Game"
    End Sub

    Friend Shared Sub Main()
        With New Program
            ' Enlarge window for the 11 players of the both team
            If .Construct(screenW:=700, screenH:=450, fullScreen:=True) Then .Start()
        End With
    End Sub

    ' Calcalate position from the center of the screen
    Private ReadOnly Property FromCenter(offsetX As Integer, offsetY As Integer) As Vi2d
        Get
            Return New Vi2d(ScreenWidth \ 2 + offsetX, ScreenHeight \ 2 + offsetY)
        End Get
    End Property

    Private ReadOnly Property IdleFrames As Dictionary(Of (String, String), Sprite)
        Get
            Return New Dictionary(Of (String, String), Sprite) From {
                {("blue_player", "move_left"), ssSoccerPlayer(0, 1)},
                {("blue_player", "move_right"), ssSoccerPlayer(1, 1)},
                {("blue_player", "move_up"), ssSoccerPlayer(2, 1)},
                {("blue_player", "move_down"), ssSoccerPlayer(3, 1)},
                {("red_player", "move_left"), ssSoccerPlayer(4, 1)},
                {("red_player", "move_right"), ssSoccerPlayer(5, 1)},
                {("red_player", "move_up"), ssSoccerPlayer(6, 1)},
                {("red_player", "move_down"), ssSoccerPlayer(7, 1)}
            }
        End Get
    End Property

    Private Shared Sub SwitchControl(team As Actor(), idx As Integer)
        For i As Integer = 0 To UBound(team)
            team(i).IsPlayerControlled = (i = idx)
        Next i
        team(idx).Velocity = New Vf2d(0, 0)
        team(idx).IsMoving = False
    End Sub

    Protected Overrides Function OnUserCreate() As Boolean
        SetPixelMode(Pixel.Mode.Mask)
        m_gameState = GameState.Title
        m_aiControllers = New List(Of AIController)
        ' Blue team's attack target (left goal)
        m_blueGoalPosition = New Vf2d(FIELD_LEFT, GOAL_POS_Y + GOAL_HEIGHT \ 2)
        ' Red team's attack target (right goal)
        m_redGoalPosition = New Vf2d(FIELD_RIGHT, GOAL_POS_Y + GOAL_HEIGHT \ 2)

        ' Load resources
        ssSoccerBall = New SpriteSheet("Assets/soccer_ball.png", New Vi2d(5, 5))
        ssSoccerPlayer = New SpriteSheet("Assets/soccer_player.png", New Vi2d(10, 15), True)

        ' Define animations for players
        ssSoccerBall.DefineAnimation("default", "moving", (0, 0), (0, 6))
        With ssSoccerPlayer
            .AddCharacterName("blue_player")
            .AddCharacterName("red_player")
            .DefineAnimation("blue_player", "move_left", (0, 0), (0, 2))
            .DefineAnimation("blue_player", "move_right", (1, 0), (1, 2))
            .DefineAnimation("blue_player", "move_up", (2, 0), (2, 2))
            .DefineAnimation("blue_player", "move_down", (3, 0), (3, 2))
            .DefineAnimation("red_player", "move_left", (4, 0), (4, 2))
            .DefineAnimation("red_player", "move_right", (5, 0), (5, 2))
            .DefineAnimation("red_player", "move_up", (6, 0), (6, 2))
            .DefineAnimation("red_player", "move_down", (7, 0), (7, 2))
        End With

        ' Initialize soccer ball
        m_soccer = New Actor(ssSoccerBall, "default", -1, PlayerPosition.Striker) With {
            .Position = FromCenter(0, 0),
            .HomePosition = FromCenter(0, 0)
        }

        ' Initialize team players
        ' (4-4-2 formation: 1 goalkeeper + 4 defenders + 4 midfielders + 2 strikers)
        InitTeamPlayers(m_bluePlayers, "blue_player", 0)
        InitTeamPlayers(m_redPlayers, "red_player", 1)

        ' Initialize AI controllers
        InitAIControllers()

        bgmMainTheme.PlayLooping()
        Return True
    End Function

    ' Initialize team players (4-4-2 formation)
    Private Sub InitTeamPlayers(team As Actor(), charaName As String, teamIdx As Integer)
        ' Position assignment are based on their indices
        ' 0-3：Defender | 4-7：Midfielder | 8-9：Striker | 10：Goalkeeper
        Dim positions = New PlayerPosition() {
            PlayerPosition.Defender, PlayerPosition.Defender, PlayerPosition.Defender,
            PlayerPosition.Defender, PlayerPosition.Midfielder, PlayerPosition.Midfielder,
            PlayerPosition.Midfielder, PlayerPosition.Midfielder, PlayerPosition.Striker,
            PlayerPosition.Striker, PlayerPosition.Goalkeeper
        }

        ' Calculate initial positions
        For i As Integer = 0 To team.Length - 1
            team(i) = New Actor(ssSoccerPlayer.DeepCopy, charaName, teamIdx, positions(i))
            With GetInitialPosition(teamIdx, i, positions(i))
                team(i).Position = New Vf2d(.X, .Y)
                team(i).HomePosition = New Vf2d(.X, .Y) ' Record tactical initial position
            End With

            ' Initialize goalkeeper properties
            If positions(i) = PlayerPosition.Goalkeeper Then
                ' Limit Y-axis range to goal height (10px buffer above and below)
                team(i).GoalkeeperYRange = (
                    GOAL_POS_Y + 10,
                    GOAL_POS_Y + GOAL_HEIGHT - 10
                )
            End If
        Next i
    End Sub

    ' Core parameters to calculate relative positions on the field
    Private ReadOnly Property FieldWidth As Integer = FIELD_RIGHT - FIELD_LEFT
    Private ReadOnly Property FieldHeight As Integer = FIELD_BOTTOM - FIELD_TOP
    Private ReadOnly Property FieldCenterX As Single = FIELD_LEFT + FieldWidth \ 2
    Private ReadOnly Property FieldCenterY As Single = FIELD_TOP + FieldHeight \ 2

    Private Function GetInitialPosition _
            (teamIdx As Integer, playerIdx As Integer, posType As PlayerPosition) _
            As (X As Single, Y As Single)
        ' Team direction: Blue team left half (attack from left to right), 
        '                 red team right half (attack from right to left)
        Dim isBlueTeam = teamIdx = 0
        Dim baseDepth = If(isBlueTeam, 0.2F, 0.8F) ' Base depth ratio (0=leftmost, 1=rightmost)

        Select Case posType
            Case PlayerPosition.Goalkeeper
                ' Goalkeeper: Directly in front of goal center, close to goal line
                Dim goalX = If(isBlueTeam, FIELD_LEFT + 10, FIELD_RIGHT - 25)
                Return (goalX, FieldCenterY) ' Strictly center in goal

            Case PlayerPosition.Defender
                ' 4 defenders: Parallel positioning, evenly cover field width (1/5 from bottom)
                Dim depth = If(posType = PlayerPosition.Defender, 0.15F, 0.3F)
                Dim x = FIELD_LEFT + FieldWidth * (If(isBlueTeam, depth, 1 - depth))
                Dim yStep = (FieldHeight * 2 / 3.0F) / 3.0F
                Dim y = FIELD_TOP + FieldHeight / 6.0F + yStep * playerIdx
                Return (x, y)

            Case PlayerPosition.Midfielder
                ' 4 midfielders: In front of defenders, evenly distribute horizontally (expand spacing)
                Dim depth = 0.3F ' Front and back position (defenders' front)
                Dim x = FIELD_LEFT + FieldWidth * (If(isBlueTeam, depth, 1 - depth))
                Dim yStep = (FieldHeight * 2 / 3.0F) / 3.0F
                Dim yOffset = If(playerIdx Mod 2 = 0, -5, 5)
                Dim y = FIELD_TOP + FieldHeight / 6.0F + yStep * (playerIdx - 4) + yOffset
                Return (x, y)

            Case PlayerPosition.Striker
                ' 2 strikers: Close to midfield, staggered positions
                Dim depth = 0.6F
                Dim x = FIELD_LEFT + FieldWidth * (If(isBlueTeam, depth, 1 - depth))
                Dim yOffset = If(playerIdx = 8, -FieldHeight / 8.0F, FieldHeight / 8.0F)
                Dim y = FieldCenterY + yOffset
                Return (x, y)

            Case Else
                Return (ScreenWidth \ 2, ScreenHeight \ 2)
        End Select
    End Function

    Private Sub InitAIControllers()
        m_aiControllers.Clear()

        ' Blue team AI (exclude current controlled player)
        For i As Integer = 0 To m_bluePlayers.Length - 1
            If i <> m_blueControlledIdx Then
                m_aiControllers.Add(New AIController(
                    m_bluePlayers(i), m_soccer,
                    m_bluePlayers.ToList(), m_redPlayers.ToList(),
                    m_redGoalPosition, m_blueGoalPosition
                ))
            End If
        Next i

        ' Red team AI (exclude current controlled player)
        For i As Integer = 0 To m_redPlayers.Length - 1
            If i <> m_redControlledIdx Then
                m_aiControllers.Add(New AIController(
                    m_redPlayers(i), m_soccer,
                    m_redPlayers.ToList(), m_bluePlayers.ToList(),
                    m_blueGoalPosition, m_redGoalPosition
                ))
            End If
        Next i
    End Sub

    Private Sub KickBackToTheField()
        ' Stop all players' movement
        For Each p In m_bluePlayers.Concat(m_redPlayers)
            p.Velocity = New Vf2d(0, 0)
        Next p

        Dim angleDegrees As Single = CSng(Rnd * 15 + 15) ' 15-30 degrees
        Dim distance As Single = CSng(Rnd * 150 + 200) ' 200-350 pixels
        Dim angleRadians As Single = angleDegrees * MathF.PI / 180.0F

        Dim isBlueTeamKick As Boolean = m_soccer.Position.x < FieldCenterX
        Select Case m_gameState
            Case GameState.CornerKick
                ' Select appropriate corner kicker (midfielder)
                Dim kicker As Actor = If(isBlueTeamKick,
                    m_bluePlayers.FirstOrDefault(
                        Function(p) p.PositionType = PlayerPosition.Midfielder),
                    m_redPlayers.FirstOrDefault(
                        Function(p) p.PositionType = PlayerPosition.Midfielder)
                )
                With New Vf2d(  ' Correction for corner kick
                    If(isBlueTeamKick, 30, -30), If(m_soccer.Position.y < FieldCenterY, 30, -30)
                )
                    If kicker IsNot Nothing Then
                        ' Position kicker near the ball
                        kicker.Position =
                            m_soccer.Position + New Vf2d(If(isBlueTeamKick, -15, 15), 0)
                        ' Calculate ball trajectory with random angle
                        Dim direction As New Vf2d(
                            CSng(Math.Cos(angleRadians + .x) * If(isBlueTeamKick, 1, -1)),
                            CSng(Math.Sin(angleRadians + .y) * If(Rand Mod 2 = 0, 1, -1))
                        )
                        m_soccer.Velocity = direction.Norm() * BALL_SPEED
                    End If
                End With
            Case GameState.GoalKick
                ' Determine goal kick team and select goalkeeper
                Dim kicker As Actor = If(
                    isBlueTeamKick,
                    m_bluePlayers.FirstOrDefault(
                        Function(p) p.PositionType = PlayerPosition.Goalkeeper),
                    m_redPlayers.FirstOrDefault(
                        Function(p) p.PositionType = PlayerPosition.Goalkeeper)
                )

                If kicker IsNot Nothing Then
                    ' Position ball near goalkeeper
                    kicker.Position =
                        m_soccer.Position + New Vf2d(If(isBlueTeamKick, -20, 20), 0)
                    ' Calculate ball trajectory with random angle
                    Dim direction As New Vf2d(
                        CSng(Math.Cos(angleRadians) * If(isBlueTeamKick, 1, -1)),
                        CSng(Math.Sin(angleRadians) * If(Rand Mod 2 = 0, 1, -1))
                    )
                    m_soccer.Velocity = direction.Norm() * BALL_SPEED
                End If
        End Select

        ' Play kick sound and return to normal game state
        sndKick.Play()
        m_gameState = GameState.Playing
    End Sub

    Protected Overrides Function OnUserUpdate(elapsedTime As Single) As Boolean
        Clear()
        Select Case m_gameState
            Case GameState.Title
                DrawTitleScreen()
                If GetKey(Key.ENTER).Pressed Then
                    m_gameState = GameState.Playing
                    ResetGame()
                End If

            Case GameState.Playing
                SpriteSheet.PauseAllAnimations = False
                UpdateGame(elapsedTime)
                DrawGame(elapsedTime)
                If GetKey(Key.P).Pressed Then m_gameState = GameState.Paused

            Case GameState.Paused
                DrawGame(elapsedTime)
                DrawString(FromCenter(-80, -20), "GAME PAUSED", Presets.Beige, 2)
                DrawString(FromCenter(-105, 10), "PRESS ""P"" AGAIN TO CONTINUE", Presets.Beige)
                SpriteSheet.PauseAllAnimations = True
                If GetKey(Key.P).Pressed Then m_gameState = GameState.Playing

            Case GameState.Result
                DrawGame(elapsedTime)
                Dim winner = If(m_blueScore > m_redScore, "BLUE", "RED")
                DrawString(FromCenter(-115, -20), $"{winner} TEAM WINS!", Presets.Yellow, 2)
                DrawString(FromCenter(-105, 10), "PRESS ENTER TO PLAY AGAIN", Presets.Yellow)
                If GetKey(Key.ENTER).Pressed Then
                    m_gameState = GameState.Playing
                    ResetGame()
                End If

            Case GameState.CornerKick
                ' Resume game after delay
                m_kickBackTimer += elapsedTime
                If m_kickBackTimer >= KICK_BACK_DELAY Then
                    KickBackToTheField()
                    m_kickBackTimer = 0
                Else
                    DrawGame(elapsedTime)
                    DrawString(FromCenter(-50, 10), "CORNER KICK", Presets.Snow, 1)
                End If

            Case GameState.GoalKick
                m_kickBackTimer += elapsedTime
                If m_kickBackTimer >= KICK_BACK_DELAY Then
                    KickBackToTheField()
                    m_kickBackTimer = 0
                Else
                    DrawGame(elapsedTime)
                    DrawString(FromCenter(-50, 10), "GOAL KICK", Presets.Snow, 1)
                End If
        End Select

        Return Not GetKey(Key.ESCAPE).Pressed
    End Function

    Private Sub DrawTitleScreen()
        DrawSprite(New Vi2d, sprSoccerField)
        FillRect(FromCenter(-198, -85), New Vi2d(398, 185), New Pixel(50, 120, 10))
        DrawString(FromCenter(-185, -80), "EASY SOCCER GAME", Presets.Snow, 3)
        DrawString(FromCenter(-175, -40), "MADE WITH vbPixelGameEngine BY PAC-DESSERT1436", Presets.Snow)
        DrawString(
            FromCenter(-190, 0),
            "BLUE TEAM: W,A,S,D TO MOVE; ""E"" TO SWITCH PLAYER", Presets.Cyan
        )
        DrawString(
            FromCenter(-190, 20),
            "RED TEAM: ARROWS TO MOVE; SPACE TO SWITCH PLAYER", Presets.Red
        )
        DrawString(FromCenter(-190, 40), "FIRST TO SCORE 3 GOALS WINS!", Presets.Yellow)
        DrawString(FromCenter(-100, 75), "* PRESS ENTER TO START *", Presets.Snow)

        If GetKey(Key.ENTER).Pressed Then
            m_gameState = GameState.Playing
            m_kickBackTimer = 0
            ResetGame()
        End If
    End Sub

    Private Sub UpdateGame(dt As Single)
        m_blueControlledIdx = Array.FindIndex(m_bluePlayers, Function(p) p.IsPlayerControlled)
        m_redControlledIdx = Array.FindIndex(m_redPlayers, Function(p) p.IsPlayerControlled)

        UpdatePlayerControls(dt)
        UpdateAIPlayers(dt)
        UpdateBall(dt)
        CheckGoals()
        CheckBallOut()
    End Sub

    Private Sub UpdatePlayerControls(dt As Single)
        ' Blue team
        HandlePlayer(m_bluePlayers, m_blueControlledIdx, Key.W, Key.S, Key.A, Key.D, Key.E, dt)

        ' Red team
        HandlePlayer(m_redPlayers, m_redControlledIdx, Key.UP, Key.DOWN, Key.LEFT, Key.RIGHT,
                     Key.SPACE, dt)
    End Sub

    ' Logic to handle the control of a single player
    Private Sub HandlePlayer _
            (team As Actor(), ByRef controlIdx As Integer, upKey As Key, downKey As Key,
             leftKey As Key, rightKey As Key, switchKey As Key, dt As Single)
        Dim player = team(controlIdx)
        Dim inputDir = New Vf2d(0, 0)

        ' For goalkeeper: Limit movement to Y-axis in the penalty area only
        If player.PositionType = PlayerPosition.Goalkeeper Then
            Dim penaltyLeft = If(player.Team = 0, FIELD_LEFT, FIELD_RIGHT - PENALTY_AREA_WIDTH)
            Dim penaltyRight = If(player.Team = 0, FIELD_LEFT + PENALTY_AREA_WIDTH, FIELD_RIGHT)

            If GetKey(upKey).Held Then inputDir.y -= 1
            If GetKey(downKey).Held Then inputDir.y += 1
            player.Velocity = New Vf2d(0, inputDir.y * PLAYER_SPEED * 0.9F)

            Dim newY = Math.Clamp(player.Position.y + player.Velocity.y * dt,
                                  player.GoalkeeperYRange.Min, player.GoalkeeperYRange.Max)
            player.Position = New Vf2d(player.Position.x, newY)
        Else
            ' Other players can freely move around
            If GetKey(upKey).Held Then inputDir.y -= 1
            If GetKey(downKey).Held Then inputDir.y += 1
            If GetKey(leftKey).Held Then inputDir.x -= 1
            If GetKey(rightKey).Held Then inputDir.x += 1

            ' Calculate player speed and direction
            If inputDir.Mag > 0 Then
                ' Multiply speed by 1.1 for strikers
                Dim speedMult = If(player.PositionType = PlayerPosition.Striker, 1.05F, 1.0F)
                player.Velocity = inputDir.Norm() * PLAYER_SPEED * speedMult
                player.IsMoving = True
                player.CurrDirection = GetDirectionFromInput(inputDir)
            Else
                player.Velocity = New Vf2d(0, 0)
                player.IsMoving = False
            End If

            ' Update position (limit within the field)
            player.Position += player.Velocity * dt
            player.Position = New Vf2d(
                Math.Clamp(player.Position.x, FIELD_LEFT, FIELD_RIGHT - 10),
                Math.Clamp(player.Position.y, FIELD_TOP, FIELD_BOTTOM)
            )
        End If

        ' Switch players (exclude goalkeepers)
        If GetKey(switchKey).Pressed Then
            Dim currIdx = controlIdx
            Do
                controlIdx = Rand Mod 10 ' Only select players 0-9 (exclude goalkeeper at index 10)
            Loop Until controlIdx <> currIdx
            ' Update the controlled indices and reset their states
            SwitchControl(team, controlIdx)
        End If

        ' Detect players' act of passing the soccer ball
        If player.Bounds.Intersects(m_soccer.Bounds) Then
            Dim kickDir = (m_soccer.Position - player.Position).Norm()
            Dim targetPos = m_soccer.Position + kickDir * BALL_SPEED

            If player.IsForwardPass(targetPos) Then
                m_soccer.Velocity = kickDir * BALL_SPEED
                sndKick.Play()
            End If
        End If
    End Sub

    Private Shared Function GetDirectionFromInput(inputDir As Vf2d) As Direction
        If Math.Abs(inputDir.x) > Math.Abs(inputDir.y) Then
            Return If(inputDir.x > 0, Direction.Right, Direction.Left)
        Else
            Return If(inputDir.y > 0, Direction.Down, Direction.Up)
        End If
    End Function

    Private Sub UpdateAIPlayers(dt As Single)
        For Each aic In m_aiControllers
            With aic.Player
                aic.Update(dt)

                .Position += .Velocity * dt

                ' Limit AI players in the field
                .Position = New Vf2d(
                    Math.Clamp(.Position.x, FIELD_LEFT, FIELD_RIGHT - 10),
                    Math.Clamp(.Position.y, FIELD_TOP, FIELD_BOTTOM)
                )
            End With
        Next aic
    End Sub

    Private Sub UpdateBall(dt As Single)
        With m_soccer
            .Velocity *= 0.97F
            .Position += .Velocity * dt
            Dim newVel = .Velocity, newPos = .Position

            ' Apply the field boundary constraints for the soccer ball (Y-axis)
            ' For ball going out of field from left or right, change to goal kick mode
            If .Position.y < FIELD_TOP OrElse .Position.y > FIELD_BOTTOM Then
                newVel.y *= -0.8F
                newPos.y = Math.Clamp(.Position.y, FIELD_TOP, FIELD_BOTTOM)
            End If

            .Velocity = newVel
            .Position = newPos
        End With
    End Sub

    Private Sub CheckGoals()
        ' Blue team
        If m_soccer.Position.x >= FIELD_RIGHT - 5 AndAlso
           m_soccer.Position.y >= GOAL_POS_Y AndAlso
           m_soccer.Position.y <= GOAL_POS_Y + GOAL_HEIGHT Then
            m_blueScore += 1
            sndGoal.Play()
            ResetAfterGoal()
        End If

        ' Red team
        If m_soccer.Position.x <= FIELD_LEFT + 5 AndAlso
           m_soccer.Position.y >= GOAL_POS_Y AndAlso
           m_soccer.Position.y <= GOAL_POS_Y + GOAL_HEIGHT Then
            m_redScore += 1
            sndGoal.Play()
            ResetAfterGoal()
        End If
    End Sub

    Private Sub CheckBallOut()
        If m_gameState <> GameState.Playing Then Exit Sub

        With m_soccer
            If .Position.x <= FIELD_LEFT OrElse .Position.x >= FIELD_RIGHT Then
                ' Detect corner kicks
                If .Position.y <= FIELD_TOP + 50 OrElse .Position.y >= FIELD_BOTTOM - 50 Then
                    m_gameState = GameState.CornerKick
                    sndWhistle.Play()
                    ' Corner kick position (field corner outside)
                    Dim cornerX = If(
                        .Position.x <= FIELD_LEFT, FIELD_LEFT + 10, FIELD_RIGHT - 10
                    )
                    Dim cornerY = If(
                       .Position.y <= FIELD_TOP + 30, FIELD_TOP + 10, FIELD_BOTTOM - 10
                    )
                    .Position = New Vf2d(cornerX, cornerY)
                    .Velocity = New Vf2d(0, 0)

                    ' Switch to the nearest player to the corner kick
                    SwitchToNearestPlayer()
                Else
                    ' Extra check to ensure the ball really crosses the goal line
                    Dim isLeftGoal = .Position.x <= FIELD_LEFT
                    Dim goalLine = If(isLeftGoal, FIELD_LEFT, FIELD_RIGHT)
                    Dim ballCrossedLine = If(isLeftGoal,
                        .Position.x + 5 < goalLine,
                        .Position.x > goalLine
                    )
                    If Not ballCrossedLine Then Exit Sub

                    m_gameState = GameState.GoalKick
                    sndWhistle.Play()
                    ' Goal kick position (penalty area)
                    Dim goalKickX = If(
                        .Position.x <= FIELD_LEFT, FIELD_LEFT + 30, FIELD_RIGHT - 30
                    )
                    .Position = New Vf2d(goalKickX, FieldCenterY)
                    .Velocity = New Vf2d(0, 0)
                    ' Switch to the nearest player to the goal kick
                    SwitchToNearestPlayer()
                End If
            End If
        End With
    End Sub

    Private Sub ResetAfterGoal()
        ' Set initial controlled players (blue team striker 1, red team striker 1)
        SwitchControl(m_bluePlayers, 9)
        SwitchControl(m_redPlayers, 9)

        If m_blueScore >= 3 OrElse m_redScore >= 3 Then
            m_gameState = GameState.Result
        Else
            ' Center the ball on the field and make sure it doesn't move
            m_soccer.Position = New Vf2d(
                (FIELD_LEFT + FIELD_RIGHT) / 2, (FIELD_TOP + FIELD_BOTTOM) / 2
            )
            m_soccer.Velocity = New Vf2d(0, 0)

            ' All the players gets back to their home positions
            For Each p In m_bluePlayers.Concat(m_redPlayers)
                p.Position = p.HomePosition
                p.Velocity = New Vf2d(0, 0)
            Next p
        End If
    End Sub

    Private Sub ResetGame()
        m_blueScore = 0
        m_redScore = 0
        ResetAfterGoal()  ' Reset all other elements
    End Sub

    Private Sub DrawGame(dt As Single)
        ' Draw the field and the both goals
        DrawSprite(New Vi2d, sprSoccerField)
        Const OFFSET As Integer = 15
        DrawSprite(New Vi2d(5, GOAL_POS_Y - OFFSET), sprGoalLeft)
        DrawSprite(New Vi2d(FIELD_RIGHT, GOAL_POS_Y - OFFSET), sprGoalRight)

        ' Draw score and the soccer ball
        DrawString(FIELD_LEFT + 65, FIELD_TOP - 15, CStr(m_blueScore), Presets.Cyan, 3)
        DrawString(FIELD_RIGHT - 85, FIELD_TOP - 15, CStr(m_redScore), Presets.Red, 3)
        With ssSoccerBall
            .PlayAnimation("default", "moving", 0.1F, dt)
            .DrawFrame(Me, "default", m_soccer.Position)
        End With

        ' Draw all players
        For Each player In m_bluePlayers.Concat(m_redPlayers)
            Dim animName = "move_" & player.CurrDirection.ToString().ToLower()
            If player.IsMoving Then
                ' Play animations
                player.SpriteSheet.PlayAnimation(player.CharaName, animName, 0.2F, dt)
                player.SpriteSheet.DrawFrame(Me, player.CharaName, player.Position)
            Else
                ' Draw idle frames
                Dim idleKey = (player.CharaName, animName)
                If IdleFrames.TryGetValue(idleKey, Nothing) Then
                    DrawSprite(player.Position, IdleFrames(idleKey))
                Else
                    DrawSprite(player.Position, player.SpriteSheet(0, 0))
                End If
            End If

            ' Mark current players
            If player.IsPlayerControlled Then
                Dim color = If(player.Team = 0, Presets.Cyan, Presets.Red)
                Dim pos = player.Position + New Vi2d(5, -5)
                FillTriangle(pos, pos + New Vi2d(-3, -3), pos + New Vi2d(3, -3), color)
            End If
        Next player
    End Sub

    Private Sub SwitchToNearestPlayer()
        Dim ballPos = m_soccer.Position
        Dim team = If(ballPos.x < FieldCenterX, m_bluePlayers, m_redPlayers)

        ' Find the nearest player who isn't the goalkeeper
        Dim search = Aggregate p In team Where p.PositionType <> PlayerPosition.Goalkeeper
                         Into Min((p.Position - ballPos).Mag())
        Dim nearestIdx = Array.FindIndex(team,
            Function(p) p.PositionType <> PlayerPosition.Goalkeeper AndAlso
                        (p.Position - ballPos).Mag() = search
        )

        If nearestIdx <> -1 Then SwitchControl(team, nearestIdx)
    End Sub
End Class