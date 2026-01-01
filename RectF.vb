Option Strict On
Option Infer On
Imports VbPixelGameEngine

Public Structure RectF
    Public x As Single, y As Single, width As Single, height As Single

    Public Sub New(x As Single, y As Single, width As Single, height As Single)
        Me.x = x
        Me.y = y
        Me.width = width
        Me.height = height
    End Sub

    Public Sub New(position As Vf2d, size As Vf2d)
        Me.New(position.x, position.y, size.x, size.y)
    End Sub

    Public ReadOnly Property Left As Single
        Get
            Return x
        End Get
    End Property

    Public ReadOnly Property Right As Single
        Get
            Return x + width
        End Get
    End Property

    Public ReadOnly Property Top As Single
        Get
            Return y
        End Get
    End Property

    Public ReadOnly Property Bottom As Single
        Get
            Return y + height
        End Get
    End Property

    Public ReadOnly Property Location As Vf2d
        Get
            Return New Vf2d(x, y)
        End Get
    End Property

    Public ReadOnly Property Size As Vf2d
        Get
            Return New Vf2d(width, height)
        End Get
    End Property

    Public ReadOnly Property Center As Vf2d
        Get
            Return New Vf2d(x + width / 2, y + height / 2)
        End Get
    End Property

    Public Function Contains(x As Single, y As Single) As Boolean
        Return x >= Left AndAlso x < Right AndAlso y >= Top AndAlso y < Bottom
    End Function

    Public Function Contains(point As Vf2d) As Boolean
        Return Contains(point.x, point.y)
    End Function

    Public Function Contains(rect As RectF) As Boolean
        Return rect.Left >= Left AndAlso rect.Right <= Right AndAlso
               rect.Top >= Top AndAlso rect.Bottom <= Bottom
    End Function

    Public Function Intersects(rect As RectF) As Boolean
        Return Left < rect.Right AndAlso Right > rect.Left AndAlso
               Top < rect.Bottom AndAlso Bottom > rect.Top
    End Function

    Public Sub Offset(x As Single, y As Single)
        Me.X += x
        Me.Y += y
    End Sub

    Public Sub Offset(point As Vf2d)
        Offset(point.x, point.y)
    End Sub

    Public Sub Inflate(dx As Single, dy As Single)
        x -= dx
        y -= dy
        width += dx * 2
        height += dy * 2
    End Sub

    Public Shared Function Intersect(a As RectF, b As RectF) As RectF
        Dim left As Single = Math.Max(a.Left, b.Left)
        Dim top As Single = Math.Max(a.Top, b.Top)
        Dim right As Single = Math.Min(a.Right, b.Right)
        Dim bottom As Single = Math.Min(a.Bottom, b.Bottom)

        Return If(right <= left OrElse bottom <= top, New RectF,
            New RectF(left, top, right - left, bottom - top))
    End Function

    Public Shared Function Union(a As RectF, b As RectF) As RectF
        Dim left As Single = Math.Min(a.Left, b.Left)
        Dim top As Single = Math.Min(a.Top, b.Top)
        Dim right As Single = Math.Max(a.Right, b.Right)
        Dim bottom As Single = Math.Max(a.Bottom, b.Bottom)

        Return New RectF(left, top, right - left, bottom - top)
    End Function

    Public ReadOnly Property IsEmpty As Boolean
        Get
            Return width <= 0 OrElse height <= 0
        End Get
    End Property

    Public Overrides Function ToString() As String
        Return $"{{ X:{x:F2} Y:{y:F2} Width:{width:F2} Height:{height:F2} }}"
    End Function
End Structure