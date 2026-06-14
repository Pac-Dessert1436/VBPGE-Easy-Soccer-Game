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

Public NotInheritable Class Actor
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

Public NotInheritable Class AIController
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
        Dim fixedX = If(m_player.Team = 0, FIELD_LEFT + 10, FIELD_RIGHT - 25)

        If IsBallInPenaltyArea() Then
            TargetPos = New Vf2d(fixedX, m_ball.Position.y)
        Else
            TargetPos = New Vf2d(fixedX, (m_player.GoalkeeperYRange.Min + m_player.GoalkeeperYRange.Max) / 2)
        End If

        Dim dir = (TargetPos - m_player.Position).Norm()
        dir.x = 0
        m_player.Velocity = dir * PLAYER_SPEED * 0.8F

        If Math.Abs(dir.y) > 0.1F Then
            m_player.IsMoving = True
            m_player.CurrDirection = If(dir.y > 0, Direction.Down, Direction.Up)
        Else
            m_player.IsMoving = False
            m_player.Velocity = New Vf2d(0, 0)
        End If

        m_player.Position = New Vf2d(fixedX, Math.Clamp(m_player.Position.y, m_player.GoalkeeperYRange.Min, m_player.GoalkeeperYRange.Max))
    End Sub

    Private ReadOnly Property IsValidPass As Boolean
        Get
            Return m_passCooldown <= 0 AndAlso m_player.IsForwardPass(TargetPos)
        End Get
    End Property

    Private Sub UpdateDefender()
        Dim nearestOpponent = Aggregate o In m_opponents
                                  Where o.PositionType <> PlayerPosition.Goalkeeper
                                  Order By (o.Position - m_ball.Position).Mag()
                                      Into FirstOrDefault()

        Dim targetPos As Vf2d

        If nearestOpponent IsNot Nothing Then
            Dim distToBall = (nearestOpponent.Position - m_ball.Position).Mag()
            If distToBall < 40 Then
                targetPos = m_ball.Position + (nearestOpponent.Position - m_ball.Position).Norm() * 20
            Else
                targetPos = If(
                    IsBallInOwnHalf(),
                    m_ball.Position + (m_defendTarget - m_ball.Position).Norm() * 35.0F,
                    m_player.HomePosition
                )
            End If
        Else
            targetPos = If(
                IsBallInOwnHalf(),
                m_ball.Position + (m_defendTarget - m_ball.Position).Norm() * 35.0F,
                m_player.HomePosition
            )
        End If

        targetPos = AvoidTeammates(targetPos)
        targetPos = New Vf2d(
            Math.Clamp(targetPos.x, FIELD_LEFT + 10, FIELD_RIGHT - 20),
            Math.Clamp(targetPos.y, FIELD_TOP + 10, FIELD_BOTTOM - 10)
        )

        TargetPos = targetPos

        Dim dir = (TargetPos - m_player.Position).Norm()
        Dim distToTarget = (TargetPos - m_player.Position).Mag()

        If distToTarget > 5 Then
            Dim speed = PLAYER_SPEED * 0.85F
            m_player.Velocity = dir * speed
            UpdateMovementState(dir)
        Else
            m_player.Velocity = New Vf2d(0, 0)
            m_player.IsMoving = False
        End If

        If IsValidPass AndAlso (m_player.Position - m_ball.Position).Mag < KICK_RANGE Then
            Dim validTargets = From p In m_teammates
                               Where p.PositionType <> PlayerPosition.Goalkeeper
                               Let dist = (p.Position - m_player.Position).Mag()
                               Where dist > 30.0F AndAlso dist < 180.0F
                               Select p

            Dim bestFit = Aggregate p In validTargets
                              Order By (m_attackTarget - p.Position).Mag()
                                  Into FirstOrDefault()
            Dim bestTarget = If(validTargets.Any(), bestFit, Nothing)

            Dim target = If(bestTarget IsNot Nothing, bestTarget.Position, m_defendTarget)
            m_ball.Velocity = (target - m_ball.Position).Norm() * BALL_SPEED * 0.9F
            m_passCooldown = PASS_COOLDOWN
        End If
    End Sub

    Private Sub UpdateMidfielder()
        Dim ballDistToHome = (m_ball.Position - m_player.HomePosition).Mag()
        Dim targetPos As Vf2d

        If IsBallInOwnHalf() AndAlso ballDistToHome < 180 Then
            targetPos = m_ball.Position + (m_defendTarget - m_ball.Position).Norm() * 25.0F
        ElseIf Not IsBallInOwnHalf() AndAlso ballDistToHome < 220 Then
            targetPos = m_ball.Position + (m_attackTarget - m_ball.Position).Norm() * 25.0F
        Else
            targetPos = m_player.HomePosition
        End If

        targetPos = AvoidTeammates(targetPos)

        Dim midfieldLeft = If(m_player.Team = 0, FIELD_LEFT + 120, FIELD_LEFT + 280)
        Dim midfieldRight = If(m_player.Team = 0, FIELD_RIGHT - 280, FIELD_RIGHT - 120)
        targetPos = New Vf2d(
            Math.Clamp(targetPos.x, midfieldLeft, midfieldRight),
            Math.Clamp(targetPos.y, FIELD_TOP + 30, FIELD_BOTTOM - 30)
        )

        TargetPos = targetPos

        Dim dir = (TargetPos - m_player.Position).Norm()
        Dim distToTarget = (TargetPos - m_player.Position).Mag()

        If distToTarget > 5 Then
            Dim speed = PLAYER_SPEED * 0.95F
            m_player.Velocity = dir * speed
            UpdateMovementState(dir)
        Else
            m_player.Velocity = New Vf2d(0, 0)
            m_player.IsMoving = False
        End If

        If IsValidPass AndAlso (m_player.Position - m_ball.Position).Mag < KICK_RANGE Then
            Dim validTargets = From p In m_teammates
                               Where p.PositionType <> PlayerPosition.Goalkeeper
                               Let dist = (p.Position - m_player.Position).Mag()
                               Where dist > 35.0F AndAlso dist < 160.0F
                               Select p

            Dim bestFit = Aggregate p In validTargets
                              Order By (m_attackTarget - p.Position).Mag()
                                  Into FirstOrDefault()
            Dim bestTarget = If(validTargets.Any(), bestFit, Nothing)

            Dim target = If(bestTarget IsNot Nothing, bestTarget.Position, m_defendTarget)
            m_ball.Velocity = (target - m_ball.Position).Norm() * BALL_SPEED * 0.95F
            m_passCooldown = PASS_COOLDOWN
        End If
    End Sub

    Private Sub UpdateStriker()
        Dim targetPos As Vf2d
        Dim distToGoal = (m_ball.Position - m_attackTarget).Mag()

        If distToGoal < 200 Then
            Dim interceptPoint = CalculateInterceptPoint()
            targetPos = If(interceptPoint <> Nothing, interceptPoint, m_ball.Position)
        Else
            Dim strikerX = If(m_player.Team = 0, FIELD_RIGHT - PENALTY_AREA_WIDTH - 15,
                              FIELD_LEFT + PENALTY_AREA_WIDTH + 15)
            Dim strikerY = m_ball.Position.y
            targetPos = New Vf2d(strikerX, strikerY)
        End If

        targetPos = AvoidTeammates(targetPos)
        targetPos = New Vf2d(
            Math.Clamp(targetPos.x, FIELD_LEFT + 50, FIELD_RIGHT - 50),
            Math.Clamp(targetPos.y, FIELD_TOP + 15, FIELD_BOTTOM - 15)
        )

        TargetPos = targetPos

        Dim dir = (TargetPos - m_player.Position).Norm()
        Dim distToTarget = (TargetPos - m_player.Position).Mag()

        If distToTarget > 5 Then
            Dim speed = PLAYER_SPEED * 1.05F
            m_player.Velocity = dir * speed
            UpdateMovementState(dir)
        Else
            m_player.Velocity = New Vf2d(0, 0)
            m_player.IsMoving = False
        End If

        If IsValidPass AndAlso (m_player.Position - m_ball.Position).Mag < KICK_RANGE Then
            Dim distToAttackTarget = (m_ball.Position - m_attackTarget).Mag()
            
            If distToAttackTarget < 150 Then
                m_ball.Velocity = (m_attackTarget - m_ball.Position).Norm() * BALL_SPEED * 1.1F
            Else
                Dim validTargets = From p In m_teammates
                                   Where p.PositionType = PlayerPosition.Striker
                                   Let dist = (p.Position - m_player.Position).Mag()
                                   Where dist > 25.0F AndAlso dist < 120.0F
                                   Select p

                Dim bestFit = Aggregate p In validTargets
                                  Order By (m_attackTarget - p.Position).Mag()
                                      Into FirstOrDefault()
                Dim bestTarget = If(validTargets.Any(), bestFit, Nothing)

                Dim target = If(bestTarget IsNot Nothing, bestTarget.Position, m_attackTarget)
                m_ball.Velocity = (target - m_ball.Position).Norm() * BALL_SPEED * 0.95F
            End If
            m_passCooldown = PASS_COOLDOWN
        End If
    End Sub

    Private Function CalculateInterceptPoint() As Vf2d
        Dim ballFuturePos = m_ball.Position + m_ball.Velocity * 2.0F
        Dim distToBall = (m_player.Position - m_ball.Position).Mag()
        Dim distToFuture = (m_player.Position - ballFuturePos).Mag()

        If distToFuture < distToBall AndAlso m_ball.Velocity.Mag() > 5 Then
            Return ballFuturePos
        End If
        Return Nothing
    End Function

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
        Dim avoidRange = 22.0F
        Dim maxAvoidDistance = 30.0F

        For Each mate In m_teammates
            Dim distToMate = (adjustedTarget - mate.Position).Mag()
            If distToMate < avoidRange AndAlso distToMate > 0 Then
                Dim awayDir = (adjustedTarget - mate.Position).Norm()
                Dim avoidDist = Math.Min(avoidRange - distToMate, maxAvoidDistance)
                
                Dim perpDir = awayDir.Perp()
                Dim randomFactor = If(Rnd() > 0.5, 1.0F, -1.0F)
                adjustedTarget += perpDir * avoidDist * randomFactor
            End If
        Next mate

        adjustedTarget = New Vf2d(
            Math.Clamp(adjustedTarget.x, FIELD_LEFT + 10, FIELD_RIGHT - 20),
            Math.Clamp(adjustedTarget.y, FIELD_TOP + 10, FIELD_BOTTOM - 10)
        )

        Return adjustedTarget
    End Function
End Class