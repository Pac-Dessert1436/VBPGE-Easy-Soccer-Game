Imports VbPixelGameEngine

Public Module Essentials
    ' Field and rule constants (according to actual soccer game proportions)
    Public Const GOAL_WIDTH As Integer = 15
    Public Const GOAL_HEIGHT As Integer = 120
    Public Const GOAL_POS_Y As Integer = 165 ' Goal Y-axis starting position: (450 - 120) \ 2
    Public Const FIELD_LEFT As Integer = 20 ' Field left boundary
    Public Const FIELD_RIGHT As Integer = 680 ' Field right boundary (widened for 11 players)
    Public Const FIELD_TOP As Integer = 20 ' Field top boundary
    Public Const FIELD_BOTTOM As Integer = 430 ' Field bottom boundary
    Public Const PENALTY_AREA_WIDTH As Integer = 60 ' Penalty area width
    Public Const PENALTY_AREA_DEPTH As Integer = 20 ' Penalty area depth

    ' Constants for movement and interaction
    Public Const PLAYER_SPEED As Single = 70.0F ' Player movement speed (adjusted by position)
    Public Const BALL_SPEED As Single = 100.0F ' Ball speed
    Public Const KICK_RANGE As Integer = 18 ' Effective kicking range
    Public Const KICK_BACK_DELAY As Single = 2.0F ' Delay of Corner kick or goal kick

    ' Player position type (for tactical logic)
    Public Enum PlayerPosition
        Goalkeeper = 0
        Defender = 1
        Midfielder = 2
        Striker = 3
    End Enum

    Public Enum GameState As Byte
        Title = 0
        Playing = 1
        Paused = 2
        Result = 3
        CornerKick = 4
        GoalKick = 5
    End Enum

    Public Enum Direction As Byte
        Left = 1
        Right = 2
        Up = 3
        Down = 4
    End Enum
End Module

Friend Class Actor
    Public ReadOnly Property SpriteSheet As SpriteSheet
    Public ReadOnly Property CharaName As String
    Public Property Position As Vf2d
    Public Property Velocity As Vf2d
    Public Property IsPlayerControlled As Boolean
    Public Property Team As Integer ' 0 for blue team and 1 for red team
    Public Property CurrDirection As Direction = Direction.Right
    Public Property IsMoving As Boolean = False
    Public Property PositionType As PlayerPosition
    Public Property GoalkeeperYRange As (Min As Single, Max As Single)
    Public Property HomePosition As Vf2d

    Public Sub New(spriteSheet As SpriteSheet, charaName As String, team As Integer,
                   positionType As PlayerPosition)
        Me.SpriteSheet = spriteSheet
        Me.CharaName = charaName
        Me.Team = team
        Me.PositionType = positionType
        Velocity = New Vf2d(0, 0)
    End Sub

    Public ReadOnly Property Bounds As RectF
        Get
            ' Soccer ball is of 5x5 size, whereas players are 10x15
            Return New RectF(Position, If(Team = -1, New Vi2d(5, 5), New Vi2d(10, 15)))
        End Get
    End Property

    Public Function IsForwardPass(targetPos As Vf2d) As Boolean
        ' Blue team's forward pass is to the right; the red team's is to the left
        Return If(Team = 0, targetPos.x > Position.x, targetPos.x < Position.x)
    End Function
End Class

Friend Class AIController
    Private ReadOnly m_player As Actor
    Private ReadOnly m_ball As Actor
    Private ReadOnly m_teammates As List(Of Actor)
    Private ReadOnly m_opponents As List(Of Actor)
    Private ReadOnly m_attackTarget As Vf2d  ' Attack target is the opponent's goal
    Private ReadOnly m_defendTarget As Vf2d  ' Defend target is their own goal
    Private m_passCooldown As Single = 0.0F
    Private Const PASS_COOLDOWN As Single = 1.5F

    Public ReadOnly Property Player As Actor
        Get
            Return m_player
        End Get
    End Property

    Public Property TargetPos As Vf2d  ' Target position for AI movement

    Public Sub New(player As Actor, ball As Actor, teammates As List(Of Actor),
                   opponents As List(Of Actor), attackTarget As Vf2d, defendTarget As Vf2d)
        m_player = player
        m_ball = ball
        m_teammates = New List(Of Actor)(From p In teammates Where p IsNot player)
        m_opponents = opponents
        m_attackTarget = attackTarget
        m_defendTarget = defendTarget
    End Sub

    Public Sub Update(dt As Single)
        m_passCooldown = Math.Max(0, m_passCooldown - dt)

        If m_player.IsPlayerControlled Then Exit Sub
        Select Case m_player.PositionType
            Case PlayerPosition.Goalkeeper
                UpdateGoalkeeper()
            Case PlayerPosition.Defender
                UpdateDefender()
            Case PlayerPosition.Midfielder
                UpdateMidfielder()
            Case PlayerPosition.Striker
                UpdateStriker()
        End Select
    End Sub

    Private Sub UpdateGoalkeeper()
        ' Restrict to own penalty area
        Dim penaltyAreaLeft = If(
            m_player.Team = 0, FIELD_LEFT, FIELD_RIGHT - PENALTY_AREA_WIDTH
        )
        Dim penaltyAreaRight = If(
            m_player.Team = 0, FIELD_LEFT + PENALTY_AREA_WIDTH, FIELD_RIGHT
        )

        ' Prioritize saving the ball when ball is in penalty area
        If IsBallInPenaltyArea() Then
            TargetPos = m_ball.Position ' Move directly toward ball
        Else
            ' Otherwise return to center of goal line
            TargetPos = New Vf2d(
                If(m_player.Team = 0, FIELD_LEFT + 10, FIELD_RIGHT - 10),
                (m_player.GoalkeeperYRange.Min + m_player.GoalkeeperYRange.Max) / 2
            )
        End If

        ' Move to target position (Y axis priority)
        Dim dir = (TargetPos - m_player.Position).Norm()
        dir.x = 0 ' Force X direction speed to 0, allow Y axis movement
        m_player.Velocity = dir * PLAYER_SPEED ' Only Y direction has speed

        ' Update state (only check Y direction movement)
        If Math.Abs(dir.y) > 0.1F Then
            m_player.IsMoving = True
            m_player.CurrDirection = If(dir.y > 0, Direction.Down, Direction.Up)
        Else
            m_player.IsMoving = False
        End If

        ' Force fixed X position (consistent with initial position)
        Dim fixedX = If(m_player.Team = 0, FIELD_LEFT + 10, FIELD_RIGHT - 25)
        m_player.Position = New Vf2d(fixedX, m_player.Position.y)
    End Sub

    Private ReadOnly Property IsValidPass As Boolean
        Get
            Return m_passCooldown <= 0 AndAlso m_player.IsForwardPass(TargetPos)
        End Get
    End Property

    Private Sub UpdateDefender()
        Dim nearestOpponent = Aggregate o In m_opponents
                                  Order By (o.Position - m_ball.Position).Mag()
                                      Into FirstOrDefault()

        If nearestOpponent IsNot Nothing AndAlso
              (nearestOpponent.Position - m_ball.Position).Mag() < 50 Then
            TargetPos = nearestOpponent.Position ' Prior intercept opponent
            Exit Sub
        End If

        ' Prioritize defensive support for ball in own half field;
        ' return to home position otherwise
        TargetPos = If(
            IsBallInOwnHalf(),
            m_ball.Position + (m_defendTarget - m_ball.Position).Norm() * 40.0F,
            m_player.HomePosition
        )
        ' Avoid teammates
        TargetPos = AvoidTeammates(TargetPos)

        ' Movement logic for defenders (at medium speed)
        Dim dir = (TargetPos - m_player.Position).Norm()
        m_player.Velocity = dir * (PLAYER_SPEED * 0.9F)
        UpdateMovementState(dir)

        ' Close kick (prioritize evasion)
        If IsValidPass AndAlso (m_player.Position - m_ball.Position).Mag < KICK_RANGE Then
            ' Screen out distance suitable targets (exclude too near and too far)
            Dim validTargets = From p In m_teammates
                               Let dist = (p.Position - m_player.Position).Mag()
                               Where dist > 30.0F AndAlso dist < 150.0F
                               Select p ' Select 30-150 pixels within range

            ' From valid targets find best pass object (nearest to opponent's goal)
            Dim bestFit = Aggregate p In validTargets
                              Order By (m_attackTarget - p.Position).Mag()
                                  Into FirstOrDefault()
            Dim bestTarget = If(validTargets.Any(), bestFit, Nothing)

            ' Choose to defend or not to pass ball for no suitable target
            Dim target = If(bestTarget IsNot Nothing, bestTarget.Position, m_defendTarget)
            m_ball.Velocity = (target - m_ball.Position).Norm() * BALL_SPEED * 0.95F
            m_passCooldown = PASS_COOLDOWN
        End If
    End Sub

    Private Sub UpdateMidfielder()
        ' Distance between ball and midfield initial position
        Dim ballDistToHome = (m_ball.Position - m_player.HomePosition).Mag()

        If IsBallInOwnHalf() AndAlso ballDistToHome < 150 Then
            ' For ball in own half field and close, prioritize defensive support
            TargetPos = m_ball.Position + (m_defendTarget - m_ball.Position).Norm() * 20.0F
        ElseIf Not IsBallInOwnHalf() AndAlso ballDistToHome < 200 Then
            ' When ball is in opponent's half, advance to support strikers
            TargetPos = m_ball.Position + (m_attackTarget - m_ball.Position).Norm() * 20.0F
        Else
            ' When ball is far away, return to exact home position
            TargetPos = m_player.HomePosition
        End If
        TargetPos = AvoidTeammates(TargetPos)

        ' Limit target position not exceeding midfield activity area (avoid running too far)
        Dim midfieldLeft = If(m_player.Team = 0, FIELD_LEFT + 150, FIELD_LEFT + 300)
        Dim midfieldRight = If(m_player.Team = 0, FIELD_RIGHT - 300, FIELD_RIGHT - 150)
        TargetPos = New Vf2d(
            Math.Clamp(TargetPos.x, midfieldLeft, midfieldRight),
            Math.Clamp(TargetPos.y, FIELD_TOP + 50, FIELD_BOTTOM - 50)
        ) ' Avoid touch of edges

        ' Movement logic for midfielders (at normal speed)
        Dim dir = (TargetPos - m_player.Position).Norm()
        m_player.Velocity = dir * PLAYER_SPEED
        UpdateMovementState(dir)

        ' Logic optimization: prioritize pass to the better teammate (not only passing
        ' to the strikers)
        If IsValidPass AndAlso (m_player.Position - m_ball.Position).Mag < KICK_RANGE Then
            ' Screen out distance suitable targets (exclude too near and too far)
            Dim validTargets = From p In m_teammates
                               Let dist = (p.Position - m_player.Position).Mag()
                               Where dist > 30.0F AndAlso dist < 150.0F
                               Select p ' Select 30-150 pixels within range

            ' From valid targets find best pass object (nearest to opponent's goal)
            Dim bestFit = Aggregate p In validTargets
                              Order By (m_attackTarget - p.Position).Mag()
                                  Into FirstOrDefault()
            Dim bestTarget = If(validTargets.Any(), bestFit, Nothing)

            ' For no valid target to evade, choose to defend or not to pass ball
            Dim target = If(bestTarget IsNot Nothing, bestTarget.Position, m_defendTarget)
            m_ball.Velocity = (target - m_ball.Position).Norm() * BALL_SPEED * 0.95F
            m_passCooldown = PASS_COOLDOWN
        End If
    End Sub

    Private Sub UpdateStriker()
        ' Always chase ball or run toward opponent's goal
        If (m_ball.Position - m_attackTarget).Mag < 200 Then
            TargetPos = m_ball.Position ' Chase ball when near goal
        Else
            ' Otherwise run toward opponent's penalty area
            TargetPos = New Vf2d(
                If(m_player.Team = 0, FIELD_RIGHT - PENALTY_AREA_WIDTH - 10,
                   FIELD_LEFT + PENALTY_AREA_WIDTH),
                m_ball.Position.y
            )
        End If
        TargetPos = AvoidTeammates(TargetPos)

        ' Movement logic for strikers (at faster speed)
        Dim dir = (TargetPos - m_player.Position).Norm()
        m_player.Velocity = dir * (PLAYER_SPEED * 1.05F)
        UpdateMovementState(dir)

        ' Prioritize shooting at goal
        If IsValidPass AndAlso (m_player.Position - m_ball.Position).Mag < KICK_RANGE Then
            m_ball.Velocity = (m_attackTarget - m_ball.Position).Norm() * BALL_SPEED * 1.05F
            ' Ball's shooting faster
            m_passCooldown = PASS_COOLDOWN
        End If
    End Sub

    Private Sub UpdateMovementState(dir As Vf2d)
        If dir.Mag <= 0.1F Then
            m_player.IsMoving = False
            Exit Sub
        End If

        m_player.IsMoving = True
        If Math.Abs(dir.x) > Math.Abs(dir.y) Then
            m_player.CurrDirection = If(dir.x > 0, Direction.Right, Direction.Left)
        Else
            m_player.CurrDirection = If(dir.y > 0, Direction.Down, Direction.Up)
        End If
    End Sub

    Private Function IsBallInOwnHalf() As Boolean
        Dim fieldCenterX = (FIELD_LEFT + FIELD_RIGHT) / 2.0F
        Return If(
            m_player.Team = 0,
            m_ball.Position.x < fieldCenterX,
            m_ball.Position.x > fieldCenterX
        )
    End Function

    Private Function IsBallInPenaltyArea() As Boolean
        Dim penaltyLeft = If(
            m_player.Team = 0, FIELD_LEFT - 20, FIELD_RIGHT - PENALTY_AREA_WIDTH - 20
        ) ' Expand left boundary
        Dim penaltyRight = If(
            m_player.Team = 0, FIELD_LEFT + PENALTY_AREA_WIDTH + 20, FIELD_RIGHT + 20
        ) ' Expand right boundary
        Return m_ball.Position.x >= penaltyLeft AndAlso
               m_ball.Position.x <= penaltyRight AndAlso
               m_ball.Position.y >= FIELD_TOP AndAlso
               m_ball.Position.y <= FIELD_BOTTOM
    End Function

    Private Function AvoidTeammates(originalTarget As Vf2d) As Vf2d
        Dim adjustedTarget = originalTarget
        Dim avoidRange = 20.0F ' Minimum team mate spacing (20 pixels)
        For Each mate In m_teammates
            ' Calculate the distance to mate
            Dim distToMate = (adjustedTarget - mate.Position).Mag()
            If distToMate < avoidRange Then
                ' Move vertically to the line perpendicular to the mate's line,
                ' to avoid overlapping
                Dim awayDir = (adjustedTarget - mate.Position).Norm().Perp()
                adjustedTarget += awayDir * (avoidRange - distToMate)
            End If
        Next mate
        Return adjustedTarget
    End Function
End Class