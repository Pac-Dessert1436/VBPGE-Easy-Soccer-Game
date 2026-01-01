Option Strict On
Option Infer On
Imports VbPixelGameEngine

Public Class SpriteSheet
    Implements ICloneable

    Private ReadOnly sheet As Sprite, frameW As Integer, frameH As Integer

    Private _rows As Integer, _columns As Integer
    Public ReadOnly Property Rows As Integer
        Get
            Return _rows
        End Get
    End Property
    Public ReadOnly Property Columns As Integer
        Get
            Return _columns
        End Get
    End Property

    Private Class AnimationHelper
        Implements ICloneable

        Public Property CurrFrame As Sprite
        Public Property AnimFrames As New Dictionary(Of String, List(Of Sprite))
        Public Property FramePointer As IEnumerator(Of Sprite)

        Protected Overrides Sub Finalize()
            FramePointer.Dispose()  ' Dispose the frame pointer for memory safety.
        End Sub

        Public Function Clone() As Object Implements ICloneable.Clone
            Dim AnimFrames As New Dictionary(Of String, List(Of Sprite))
            For Each kvp In Me.AnimFrames
                AnimFrames.Add(kvp.Key, New List(Of Sprite)(kvp.Value))
            Next kvp
            Return New AnimationHelper With {
                .CurrFrame = CurrFrame,
                .AnimFrames = AnimFrames,
                .FramePointer = FramePointer
            }
        End Function
    End Class

    Private gameCharacters As New Dictionary(Of String, AnimationHelper)
    Private Property AllFrameIndices As (rowIdx As Integer, colIdx As Integer)()

    Private Shared _pauseAllAnim As Boolean
    Public Shared Property PauseAllAnimations As Boolean
        Private Get
            Return _pauseAllAnim
        End Get
        Set(value As Boolean)
            _pauseAllAnim = value
        End Set
    End Property

    Public Sub New(sprite As Sprite, frameScale As Vi2d, Optional noDefault As Boolean = False)
        sheet = sprite
        frameW = frameScale.x
        frameH = frameScale.y

        _columns = sheet.Width \ frameW
        _rows = sheet.Height \ frameH

        ReDim AllFrameIndices(Rows * Columns - 1)
        Dim i As Integer = 0
        For row As Integer = 0 To Rows - 1
            For col As Integer = 0 To Columns - 1
                AllFrameIndices(i) = (row, col)
                i += 1
            Next col
        Next row
        ' Add the default character name without specifying the `noDefault` parameter.
        If Not noDefault Then AddCharacterName("default")
    End Sub

    Public Sub New(imgPath As String, frameScale As Vi2d, Optional noDefault As Boolean = False)
        Me.New(New Sprite(imgPath), frameScale, noDefault)
    End Sub

    Default Public ReadOnly Property Frame(rowIdx As Integer, colIdx As Integer) As Sprite
        Get
            ArgumentNullException.ThrowIfNull(Me)
            If rowIdx < 0 OrElse rowIdx >= Rows Then
                Throw New ArgumentException(
          $"Row index is out of range; the current sprite sheet has {Rows} rows.")
            ElseIf colIdx < 0 OrElse colIdx >= Columns Then
                Throw New ArgumentException(
          $"Column index is out of range; the current sprite sheet has {Columns} columns.")
            End If
            Dim output As New Sprite(frameW, frameH)
            For i As Integer = 0 To frameW - 1
                For j As Integer = 0 To frameH - 1
                    Dim xPos As Integer = colIdx * frameW + i
                    Dim yPos As Integer = rowIdx * frameH + j
                    output.SetPixel(i, j, sheet.GetPixel(xPos, yPos))
                Next j
            Next i
            Return output
        End Get
    End Property

    Public Function CreateTileMap _
        (start As (row As Integer, col As Integer), [end] As (row As Integer, col As Integer)) _
        As List(Of Sprite)

        Dim outputTileMap As New List(Of Sprite)

        Dim tileMapStartIdx = start.row * Columns + start.col
        Dim tileMapEndIdx = [end].row * Columns + [end].col

        For idx As Integer = tileMapStartIdx To tileMapEndIdx
            Dim row = AllFrameIndices(idx).rowIdx, col = AllFrameIndices(idx).colIdx
            outputTileMap.Add(Frame(row, col))
        Next idx
        Return outputTileMap
    End Function

    Public Sub AddCharacterName(charaName As String)
        gameCharacters.Add(charaName, New AnimationHelper)
    End Sub

    Public Sub DefineAnimation(charaName As String, animName As String,
           start As (row As Integer, col As Integer), [end] As (row As Integer, col As Integer),
           Optional update As Boolean = False)

        Dim animHelper As AnimationHelper = Nothing

        If Not gameCharacters.TryGetValue(charaName, animHelper) Then
            Throw New InvalidOperationException(
                $"Character '{charaName}' does not exist. Please add it first.")
        ElseIf animHelper.AnimFrames.ContainsKey(animName) AndAlso Not update Then
            Throw New InvalidOperationException(
                $"Animation '{animName}' for the character '{charaName}' already exists. " &
                "Set the parameter 'update' to True if you want to update it.")
        End If
        gameCharacters(charaName).AnimFrames(animName) = CreateTileMap(start, [end])
    End Sub

    Public Sub PlayAnimation(charaName As String, animName As String, frameDuration As Single,
                             dt As Single, Optional isLooping As Boolean = True,
                             Optional isAnimPaused As Boolean = False)
        Static prevAnimName As String

        With gameCharacters(charaName)
            ArgumentNullException.ThrowIfNull(.AnimFrames)
            If animName <> prevAnimName Then
                If .FramePointer IsNot Nothing Then .FramePointer.Reset()
                .FramePointer = .AnimFrames(animName).GetEnumerator()
                prevAnimName = animName
                .FramePointer.MoveNext()
            End If

            Static frameTimer As Single = 0F
            If .AnimFrames(animName).Count = 0 Then
                Throw New InvalidOperationException(
                    "Current animation has no frames and cannot be played.")
            End If
            .CurrFrame = .FramePointer.Current
            If isAnimPaused OrElse PauseAllAnimations Then Exit Sub
            frameTimer += dt
            If frameTimer < frameDuration Then Exit Sub

            If .FramePointer.MoveNext() Then
                frameTimer = 0
            ElseIf isLooping Then
                .FramePointer.Reset()
                .FramePointer.MoveNext()  ' Move to the first frame.
            End If
        End With
    End Sub

    Public Sub DrawFrame(pge As PixelGameEngine, charaName As String, pos As Vf2d,
                         Optional scale As Integer = 1)
        pge.DrawSprite(pos.x, pos.y, gameCharacters(charaName).CurrFrame, scale)
    End Sub

    Public Function Clone() As Object Implements ICloneable.Clone
        Dim gameCharacters As New Dictionary(Of String, AnimationHelper)
        For Each kvp In Me.gameCharacters
            gameCharacters.Add(kvp.Key, CType(kvp.Value.Clone(), AnimationHelper))
        Next kvp

        Return New SpriteSheet(sheet, New Vi2d(frameW, frameH), True) With {
            ._rows = Rows,
            ._columns = Columns,
            .AllFrameIndices = AllFrameIndices,
            .gameCharacters = gameCharacters
        }
    End Function

    Public ReadOnly Property DeepCopy As SpriteSheet
        Get
            Return CType(Clone(), SpriteSheet)
        End Get
    End Property
End Class
