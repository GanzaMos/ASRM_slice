using System.Collections.Generic;
using BzKovSoft.ObjectSlicer;
using UnityEngine;

public class GameManager : MonoBehaviour
{
    public static GameManager Instance;

    public GameManager()
    {
        Instance = this;
    }
    
    [Space(5f)]
    [SerializeField] Transform slicer;
    [SerializeField] Transform sliceObject;

    [Space(5f)]
    [Header ("Slice rotation settings")]

    [Space(5f)]
    [SerializeField] AnimationCurve sliceOffsetByThickness;
    
    [Tooltip("It's a virtual 'radius' around which the slice rotation take place. The lower radius - the stronger slice rolling")]
    [SerializeField] float minRollRadius = 0.5f;
    
    [Tooltip("It's a virtual 'radius' around which the slice rotation take place. The bigger radius - the weaker slice rolling")]
    [SerializeField] float maxRollRadius = 3f;

    [Space(5f)]
    [SerializeField] float minPointX = 0;
    [SerializeField] float maxPointX = 0.3f;
    
    [Space(5f)]
    [SerializeField] float minDeviation = 0f;
    [SerializeField] float maxDeviation = 0.3f;
    
    [Space(5f)]
    [Tooltip("The thickness of the slice after which it will be no difference in slice rotation strength")]
    [SerializeField] float maxSliceThickness = 0.5f;
        
    
    //Locals
    Vector3 _knifeStartPosition;
    Vector3 _knifeSliceObjectPosition;
    bool _inProgress;
    GameObject _slice;
    Material[] _materials;
    float _pointY;

    public Dictionary<GameObject, MatProp> _unfinishedSlices = new Dictionary<GameObject, MatProp>();

    public class MatProp
    {
        public int SliceOrder;
        public float PointX;
        public float PointY;
        public float Deviation;
        public float Radius;
        public float Thickness;
        public Material[] Materials;
    }

    void Awake()
    {
        _knifeStartPosition = slicer.position;
        _knifeSliceObjectPosition = sliceObject.position;
    }

    public void Cut(GameObject target)
    {
        var sliceable = target.GetComponent<IBzSliceable>();
        if (sliceable == null) return;

        Plane plane = new Plane(Vector3.forward, slicer.position);
        sliceable.Slice(plane, r =>
        {
            if (!r.sliced) return;

            _inProgress = true;
            _pointY = float.MaxValue;
            _slice = r.outObjectPos;
            _materials = _slice.GetComponent<MeshRenderer>().materials;
            var meshFilter = _slice.GetComponent<MeshFilter>();
            
            AddToUnfinishedSlices(_slice);
            SetSliceMaterialProps(meshFilter.sharedMesh);
        });
    }
    
    
    //Adding unfinished slice to the slices dictionary. 
    //It needs for correct slices movement and material property setting
    void AddToUnfinishedSlices(GameObject slice)
    {
        if (_unfinishedSlices.ContainsKey(slice)) return;
        _unfinishedSlices.Add(slice, new MatProp());
        
        //Set Slice Order Value. The next slime will check this slice mat parameters, so they will match properly
        _unfinishedSlices[slice].SliceOrder = _unfinishedSlices.Count;
        _unfinishedSlices[slice].PointY = _pointY;
        _unfinishedSlices[slice].Materials = _materials;
    }
    
    
    //Setting all materials' parameters in current slice
    //All these props use for shader vertex function
    //Less thick slices will roll more intensively, more thick - less intensively
    void SetSliceMaterialProps(Mesh mesh)
    {
        //Get slice thickness
        float sliceThickness = GetSliceThickness(mesh);
        
        //Set it in the dictionary. It will be needed for next unfinished slices
        _unfinishedSlices[_slice].Thickness = sliceThickness;
        
        //Check how much will change vertex parameters in materials
        float thicknessFactor = Mathf.Clamp01(sliceThickness / maxSliceThickness);
        float matPropFactor = sliceOffsetByThickness.Evaluate(thicknessFactor);
        
        //Get these parameters
        float pointX = GetPointX(matPropFactor);
        float deviation = GetDeviation(matPropFactor);
        float radius = GetRadius(matPropFactor);
        
        
        //Have several unfinished slices? Now check previous slice in the Dictionary
        if (_unfinishedSlices.Count > 1)
        {
            //Check them all
            foreach (var slice in _unfinishedSlices)
            {
                //Find previous one
                if (slice.Value.SliceOrder == _unfinishedSlices[_slice].SliceOrder - 1)
                {
                    //Current slice less thick than previous one?
                    //Set current slice params same as in the previous one
                    //Otherwise - current slice could go through the previous one, causing glitching effect
                    if (_unfinishedSlices[_slice].Thickness < slice.Value.Thickness)
                    {
                        pointX = slice.Value.PointX;
                        deviation = slice.Value.Deviation;
                        radius = slice.Value.Radius;
                    }
                }
            }
        }

        //set current slice final parameters in the dictionary
        _unfinishedSlices[_slice].PointX = pointX;
        _unfinishedSlices[_slice].Deviation = deviation;
        _unfinishedSlices[_slice].Radius = radius;
        
        //apply these parameters to all materials in the slice
        foreach (var material in _materials)
        {
            material.SetFloat("_PointX", pointX);
            material.SetFloat("_Deviation", deviation);
            material.SetFloat("_Radius", radius);
        }
    }

    //return object lenght by Z axis, using most extreme -Z and +Z vertex coordinates in object mesh
    float GetSliceThickness(Mesh mesh)
    {
        Vector3[] vertices = mesh.vertices;

        float minZ = float.MaxValue;
        float maxZ = float.MinValue;

        // Iterate over all vertices and find the minimum and maximum X coordinates
        for (int i = 0; i < vertices.Length; i++)
        {
            float z = vertices[i].z;
            minZ = Mathf.Min(minZ, z);
            maxZ = Mathf.Max(maxZ, z);
        }

        float lengthZ = Mathf.Abs(maxZ - minZ);
        //Debug.Log("Length along X-axis: " + lengthZ);
        return lengthZ;
    }
    
    float GetRadius(float matPropertyFactor)
    {
        return Mathf.Lerp(minRollRadius, maxRollRadius, matPropertyFactor);
    }

    float GetDeviation(float matPropertyFactor)
    {
        return Mathf.Lerp(minDeviation, maxDeviation, matPropertyFactor);
    }

    float GetPointX(float matPropertyFactor)
    {
        return Mathf.Lerp(minPointX, maxPointX, matPropertyFactor);
    }

    
    //driven by Input Event
    public void MoveSlicer(float yOffset)
    {
        slicer.position = _knifeStartPosition - new Vector3(0, yOffset, 0);

        //Do all the code below IF we already cut the sliceable object AND this cut is not finished yet
        if (!_inProgress) return;
        
        //the Y point where the slicer is now 
        var pos = slicer.position;
        
        //the Y point where the slicer is now - but in local coordinates of the sliceable object
        float pointY = _slice.transform.InverseTransformPoint(pos).y;

        //_pointY = maxValue when the cut get started
        if (pointY < _pointY)
            _pointY = _unfinishedSlices[_slice].PointY = pointY;

        //set the lowest Y point of the cut to current slice material, so it will roll from this point
        SetMaterialsYPositionProp(_materials);
        
        //Check if any previous slice have higher cut point
        foreach (var slice in _unfinishedSlices)
        {
            //skip yourself
            if(slice.Key == _slice) return;

            if (slice.Value.PointY > pointY)
                //Yes? Set it to current lower cut point value 
                SetMaterialsYPositionProp(slice.Value.Materials);
        }
    }

    void SetMaterialsYPositionProp(Material[] materials)
    {
        foreach (var material in materials)
            material.SetFloat("_PointY", _pointY);
    }


    //driven by OnTriggerEnter function in the slicer script when it touches the table
    public void OnTableTouch()
    {
        //drop down all the slices
        foreach (var slice in _unfinishedSlices)
        {
            slice.Key.GetComponent<Rigidbody>().isKinematic = false;
            slice.Key.GetComponent<Rigidbody>().useGravity = true;
        }
        
        _inProgress = false;
        _unfinishedSlices.Clear();
    }

    
    public void MoveSliceObject(float zOffset)
    {
        var moveVector = new Vector3(0, 0, zOffset);
        
        //move the main untouched piece
        sliceObject.position = _knifeSliceObjectPosition + moveVector;

        //move all the unfinished slices
        foreach (var slice in _unfinishedSlices)
            slice.Key.transform.position = _knifeSliceObjectPosition + moveVector;
    }
}
