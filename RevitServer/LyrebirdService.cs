﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.ServiceModel;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Autodesk.Revit;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using LMNA.Lyrebird.LyrebirdCommon;
using Autodesk.Revit.DB.ExtensibleStorage;

namespace LMNA.Lyrebird
{
    [ServiceBehavior]
    public class LyrebirdService : ILyrebirdService
    {
        private string currentDocName = "NULL";
        private List<RevitObject> familyNames = new List<RevitObject>();
        private List<string> typeNames = new List<string>();
        private List<RevitParameter> parameters = new List<RevitParameter>();
        private List<LyrebirdId> uniqueIds = new List<LyrebirdId>();

        FamilyInstance hostFinder = null;

        private Guid instanceSchemaGUID = new Guid("9ab787e0-1660-40b7-9453-94e1043b58db");

        private static readonly object _locker = new object();

        private const int WAIT_TIMEOUT = 200;

        public List<RevitObject> GetFamilyNames()
        {
            familyNames.Add(new RevitObject("NULL", "NULL"));
            lock (_locker)
            {
                try
                {
                    UIApplication uiApp = LMNA.Lyrebird.RevitServerApp.UIApp;
                    familyNames = new List<RevitObject>();

                    // Get all standard wall families
                    FilteredElementCollector familyCollector = new FilteredElementCollector(uiApp.ActiveUIDocument.Document);
                    familyCollector.OfClass(typeof(Family));
                    List<RevitObject> families = new List<RevitObject>();
                    foreach (Family f in familyCollector)
                    {
                        RevitObject ro = new RevitObject(f.FamilyCategory.Name, f.Name);
                        families.Add(ro);
                    }

                    // Add System families
                    RevitObject wallObj = new RevitObject("Walls", "Basic Wall");
                    families.Add(wallObj);
                    RevitObject curtainObj = new RevitObject("Walls", "Curtain Wall");
                    families.Add(curtainObj);
                    RevitObject stackedObj = new RevitObject("Walls", "Stacked Wall");
                    families.Add(stackedObj);
                    RevitObject floorObj = new RevitObject("Floors", "Floor");
                    families.Add(floorObj);
                    RevitObject roofObj = new RevitObject("Roofs", "Roof");
                    families.Add(roofObj);

                    families.Sort((x, y) => String.Compare(x.FamilyName, y.FamilyName));
                    familyNames = families;
                }
                catch
                {

                }
            Monitor.Wait(_locker, WAIT_TIMEOUT);
            }
            return familyNames;
        }

        public List<string> GetTypeNames(RevitObject revitFamily)
        {
            typeNames.Add("NULL");
            lock (_locker)
            {
                try
                {
                    UIApplication uiApp = LMNA.Lyrebird.RevitServerApp.UIApp;
                    var doc = uiApp.ActiveUIDocument.Document;
                    typeNames = new List<string>();
                    List<string> types = new List<string>();
                    if (revitFamily.Category == "Walls")
                    {
                        // get wall types
                        FilteredElementCollector wallCollector = new FilteredElementCollector(uiApp.ActiveUIDocument.Document);
                        wallCollector.OfClass(typeof(WallType));
                        wallCollector.OfCategory(BuiltInCategory.OST_Walls);
                        foreach (WallType wt in wallCollector)
                        {
                            types.Add(wt.Name);
                        }
                    }

                    else if (revitFamily.Category == "Floors")
                    {
                        // Get floor types
                        FilteredElementCollector floorCollector = new FilteredElementCollector(uiApp.ActiveUIDocument.Document);
                        floorCollector.OfClass(typeof(FloorType));
                        floorCollector.OfCategory(BuiltInCategory.OST_Floors);
                        foreach (FloorType ft in floorCollector)
                        {
                            types.Add(ft.Name);
                        }
                    }

                    else if (revitFamily.Category == "Roofs")
                    {
                        // Get roof types
                        FilteredElementCollector roofCollector = new FilteredElementCollector(uiApp.ActiveUIDocument.Document);
                        roofCollector.OfClass(typeof(RoofType));
                        roofCollector.OfCategory(BuiltInCategory.OST_Roofs);
                        foreach (RoofType rt in roofCollector)
                        {
                            types.Add(rt.Name);
                        }
                    }
                    else
                    {
                        // Get typical family types.
                        FilteredElementCollector familyCollector = new FilteredElementCollector(doc);
                        familyCollector.OfClass(typeof(Family));
                        foreach (Family f in familyCollector)
                        {
                            if (f.Name == revitFamily.FamilyName)
                            {
                                FamilySymbolSet fss = f.Symbols;
                                foreach (FamilySymbol fs in fss)
                                {
                                    types.Add(fs.Name);
                                }

                                break;
                            }
                        }
                    }
                    typeNames = types;
                }
                catch
                {
                }
            Monitor.Wait(_locker, WAIT_TIMEOUT);
            }
            return typeNames;
        }

        public List<RevitParameter> GetParameters(RevitObject revitFamily, string typeName)
        {
            lock (_locker)
            {
                TaskContainer.Instance.EnqueueTask(uiApp =>
                {
                    var doc = uiApp.ActiveUIDocument.Document;
                    parameters = new List<RevitParameter>();
                    if (revitFamily.Category == "Walls")
                    {
                        // do stuff for walls
                        FilteredElementCollector wallCollector = new FilteredElementCollector(uiApp.ActiveUIDocument.Document);
                        wallCollector.OfClass(typeof(WallType));
                        wallCollector.OfCategory(BuiltInCategory.OST_Walls);
                        foreach (WallType wt in wallCollector)
                        {
                            if (wt.Name == typeName)
                            {
                                // Get the type parameters
                                List<Parameter> typeParams = new List<Parameter>();
                                foreach (Parameter p in wt.Parameters)
                                {
                                    typeParams.Add(p);
                                }

                                // Get the instance parameters
                                List<Parameter> instParameters = new List<Parameter>();
                                using (Transaction t = new Transaction(doc, "temp family"))
                                {
                                    t.Start();
                                    Wall wall = null;
                                    try
                                    {
                                        Curve c = Line.CreateBound(new XYZ(0, 0, 0), new XYZ(1, 0, 0));
                                        FilteredElementCollector lvlCollector = new FilteredElementCollector(doc);
                                        Level l = lvlCollector.OfClass(typeof(Level)).ToElements().OfType<Level>().FirstOrDefault();
                                        wall = Wall.Create(doc, c, l.Id, false);
                                    }
                                    catch
                                    {
                                        // Failed to create the wall, no instance parameters will be found
                                    }
                                    if (wall != null)
                                    {
                                        foreach (Parameter p in wall.Parameters)
                                        {
                                            instParameters.Add(p);
                                        }
                                    }
                                    t.RollBack();
                                }
                                typeParams.Sort((x, y) => String.Compare(x.Definition.Name, y.Definition.Name));
                                instParameters.Sort((x, y) => String.Compare(x.Definition.Name, y.Definition.Name));
                                foreach (Parameter p in typeParams)
                                {
                                    RevitParameter rp = new RevitParameter();
                                    rp.ParameterName = p.Definition.Name;
                                    rp.StorageType = p.StorageType.ToString();
                                    rp.IsType = true;
                                    parameters.Add(rp);
                                }
                                foreach (Parameter p in instParameters)
                                {
                                    RevitParameter rp = new RevitParameter();
                                    rp.ParameterName = p.Definition.Name;
                                    rp.StorageType = p.StorageType.ToString();
                                    rp.IsType = false;
                                    parameters.Add(rp);
                                }
                                break;
                            }
                        }
                    }
                    else if (revitFamily.Category == "Floors")
                    {
                        // get parameters for floors
                        FilteredElementCollector floorCollector = new FilteredElementCollector(doc);
                        floorCollector.OfClass(typeof(FloorType));
                        floorCollector.OfCategory(BuiltInCategory.OST_Floors);
                        foreach (FloorType ft in floorCollector)
                        {
                            if (ft.Name == typeName)
                            {
                                // Get the type parameters
                                List<Parameter> typeParams = new List<Parameter>();
                                foreach (Parameter p in ft.Parameters)
                                {
                                    typeParams.Add(p);
                                }

                                // Get the instance parameters
                                List<Parameter> instParameters = new List<Parameter>();
                                using (Transaction t = new Transaction(doc, "temp family"))
                                {
                                    t.Start();
                                    Floor floor = null;
                                    try
                                    {
                                        Curve c1 = Line.CreateBound(new XYZ(0, 0, 0), new XYZ(1, 0, 0));
                                        Curve c2 = Line.CreateBound(new XYZ(0, 1, 0), new XYZ(1, 1, 0));
                                        Curve c3 = Line.CreateBound(new XYZ(1, 1, 0), new XYZ(0, 1, 0));
                                        Curve c4 = Line.CreateBound(new XYZ(0, 1, 0), new XYZ(0, 0, 0));
                                        CurveArray profile = new CurveArray();
                                        profile.Append(c1);
                                        profile.Append(c2);
                                        profile.Append(c3);
                                        profile.Append(c4);
                                        floor = doc.Create.NewFloor(profile, false);
                                    }
                                    catch
                                    {
                                        // Failed to create the wall, no instance parameters will be found
                                    }
                                    if (floor != null)
                                    {
                                        foreach (Parameter p in floor.Parameters)
                                        {
                                            instParameters.Add(p);
                                        }
                                    }
                                    t.RollBack();
                                }
                                typeParams.Sort((x, y) => String.Compare(x.Definition.Name, y.Definition.Name));
                                instParameters.Sort((x, y) => String.Compare(x.Definition.Name, y.Definition.Name));
                                foreach (Parameter p in typeParams)
                                {
                                    RevitParameter rp = new RevitParameter();
                                    rp.ParameterName = p.Definition.Name;
                                    rp.StorageType = p.StorageType.ToString();
                                    rp.IsType = true;
                                    parameters.Add(rp);
                                }
                                foreach (Parameter p in instParameters)
                                {
                                    RevitParameter rp = new RevitParameter();
                                    rp.ParameterName = p.Definition.Name;
                                    rp.StorageType = p.StorageType.ToString();
                                    rp.IsType = false;
                                    parameters.Add(rp);
                                }
                                break;
                            }
                        }
                    }
                    else if (revitFamily.Category == "Roofs")
                    {
                        // get parameters for a roof
                        FilteredElementCollector roofCollector = new FilteredElementCollector(doc);
                        roofCollector.OfClass(typeof(RoofType));
                        roofCollector.OfCategory(BuiltInCategory.OST_Roofs);
                        foreach (RoofType rt in roofCollector)
                        {
                            if (rt.Name == typeName)
                            {
                                // Get the type parameters
                                List<Parameter> typeParams = new List<Parameter>();
                                foreach (Parameter p in rt.Parameters)
                                {
                                    typeParams.Add(p);
                                }

                                // Get the instance parameters
                                List<Parameter> instParameters = new List<Parameter>();
                                using (Transaction t = new Transaction(doc, "temp family"))
                                {
                                    t.Start();
                                    FootPrintRoof roof = null;
                                    try
                                    {
                                        Curve c1 = Line.CreateBound(new XYZ(0, 0, 0), new XYZ(1, 0, 0));
                                        Curve c2 = Line.CreateBound(new XYZ(0, 1, 0), new XYZ(1, 1, 0));
                                        Curve c3 = Line.CreateBound(new XYZ(1, 1, 0), new XYZ(0, 1, 0));
                                        Curve c4 = Line.CreateBound(new XYZ(0, 1, 0), new XYZ(0, 0, 0));
                                        CurveArray profile = new CurveArray();
                                        profile.Append(c1);
                                        profile.Append(c2);
                                        profile.Append(c3);
                                        profile.Append(c4);
                                        FilteredElementCollector lvlCollector = new FilteredElementCollector(doc);
                                        Level l = lvlCollector.OfClass(typeof(Level)).ToElements().OfType<Level>().FirstOrDefault();
                                        ModelCurveArray curveArrayMapping = new ModelCurveArray();
                                        roof = doc.Create.NewFootPrintRoof(profile, l, rt, out curveArrayMapping);
                                    }
                                    catch
                                    {
                                        // Failed to create the wall, no instance parameters will be found
                                    }
                                    if (roof != null)
                                    {
                                        foreach (Parameter p in roof.Parameters)
                                        {
                                            instParameters.Add(p);
                                        }
                                    }
                                    t.RollBack();
                                }
                                typeParams.Sort((x, y) => String.Compare(x.Definition.Name, y.Definition.Name));
                                instParameters.Sort((x, y) => String.Compare(x.Definition.Name, y.Definition.Name));
                                foreach (Parameter p in typeParams)
                                {
                                    RevitParameter rp = new RevitParameter();
                                    rp.ParameterName = p.Definition.Name;
                                    rp.StorageType = p.StorageType.ToString();
                                    rp.IsType = true;
                                    parameters.Add(rp);
                                }
                                foreach (Parameter p in instParameters)
                                {
                                    RevitParameter rp = new RevitParameter();
                                    rp.ParameterName = p.Definition.Name;
                                    rp.StorageType = p.StorageType.ToString();
                                    rp.IsType = false;
                                    parameters.Add(rp);
                                }
                                break;
                            }
                        }
                    }
                    else
                    {
                        // Regular family.  Proceed to get all parameters
                        // TODO: There are different options for different hostings.  
                        //       Make sure you account for this while creating the instance to get the inst parameters.
                        FilteredElementCollector familyCollector = new FilteredElementCollector(doc);
                        familyCollector.OfClass(typeof(Family));
                        foreach (Family f in familyCollector)
                        {
                            if (f.Name == revitFamily.FamilyName)
                            {
                                FamilySymbolSet fss = f.Symbols;
                                foreach (FamilySymbol fs in fss)
                                {
                                    if (fs.Name == typeName)
                                    {
                                        List<Parameter> typeParams = new List<Parameter>();
                                        foreach (Parameter p in fs.Parameters)
                                        {
                                            typeParams.Add(p);
                                        }
                                        List<Parameter> instanceParams = new List<Parameter>();
                                        // temporary create an instance of the family to get instance parameters
                                        using (Transaction t = new Transaction(doc, "temp family"))
                                        {
                                            t.Start();
                                            FamilyInstance fi = null;
                                            // Get the hosting type
                                            int hostType = f.get_Parameter(BuiltInParameter.FAMILY_HOSTING_BEHAVIOR).AsInteger();
                                            if (hostType == 0)
                                            {
                                                // Typical
                                            }
                                            else if (hostType == 1)
                                            {
                                                // Wall hosted
                                                // Temporary wall
                                                Wall wall = null;
                                                Curve c = Line.CreateBound(new XYZ(-20, 0, 0), new XYZ(20, 0, 0));
                                                FilteredElementCollector lvlCollector = new FilteredElementCollector(doc);
                                                Level l = lvlCollector.OfClass(typeof(Level)).ToElements().OfType<Level>().FirstOrDefault();
                                                try
                                                {
                                                    wall = Wall.Create(doc, c, l.Id, false);
                                                }
                                                catch
                                                {
                                                    // Failed to create the wall, no instance parameters will be found
                                                }
                                                if (wall != null)
                                                {
                                                    fi = doc.Create.NewFamilyInstance(new XYZ(0, 0, 0), fs, wall as Element, l, Autodesk.Revit.DB.Structure.StructuralType.NonStructural);
                                                }
                                                else
                                                {
                                                    // regular creation.  SOme parameters will be missing
                                                    fi = doc.Create.NewFamilyInstance(XYZ.Zero, fs, Autodesk.Revit.DB.Structure.StructuralType.NonStructural);
                                                }
                                                //fi = doc.Create.NewFamilyInstance(origin, fs, host, level, false)
                                            }
                                            else if (hostType == 2)
                                            {
                                                // Floor Hosted
                                                // temporary floor
                                                Floor floor = null;
                                                FilteredElementCollector lvlCollector = new FilteredElementCollector(doc);
                                                Level l = lvlCollector.OfClass(typeof(Level)).ToElements().OfType<Level>().FirstOrDefault();
                                                try
                                                {
                                                    Curve c1 = Line.CreateBound(new XYZ(0, 0, 0), new XYZ(1, 0, 0));
                                                    Curve c2 = Line.CreateBound(new XYZ(0, 1, 0), new XYZ(1, 1, 0));
                                                    Curve c3 = Line.CreateBound(new XYZ(1, 1, 0), new XYZ(0, 1, 0));
                                                    Curve c4 = Line.CreateBound(new XYZ(0, 1, 0), new XYZ(0, 0, 0));
                                                    CurveArray profile = new CurveArray();
                                                    profile.Append(c1);
                                                    profile.Append(c2);
                                                    profile.Append(c3);
                                                    profile.Append(c4);
                                                    floor = doc.Create.NewFloor(profile, false);
                                                }
                                                catch
                                                {
                                                    // Failed to create the wall, no instance parameters will be found
                                                }
                                                if (floor != null)
                                                {
                                                    fi = doc.Create.NewFamilyInstance(new XYZ(0, 0, 0), fs, floor as Element, l, Autodesk.Revit.DB.Structure.StructuralType.NonStructural);
                                                }
                                                else
                                                {
                                                    // regular creation.  SOme parameters will be missing
                                                    fi = doc.Create.NewFamilyInstance(XYZ.Zero, fs, Autodesk.Revit.DB.Structure.StructuralType.NonStructural);
                                                }
                                            }
                                            else if (hostType == 3)
                                            {
                                                // Ceiling Hosted (might be difficult)
                                            }
                                            else if (hostType == 4)
                                            {
                                                // Roof Hosted
                                            }
                                            else if (hostType == 5)
                                            {
                                                // Face Based.
                                            }
                                            // Create a typical family instance
                                            try
                                            {
                                                fi = doc.Create.NewFamilyInstance(XYZ.Zero, fs, Autodesk.Revit.DB.Structure.StructuralType.NonStructural);
                                            }
                                            catch (Exception ex)
                                            {

                                            }
                                            // TODO: Try creating other family instances like walls, sketch based, ... and getting the instance params
                                            foreach (Parameter p in fi.Parameters)
                                            {
                                                instanceParams.Add(p);
                                            }
                                            t.RollBack();
                                        }

                                        typeParams.Sort((x, y) => String.Compare(x.Definition.Name, y.Definition.Name));
                                        instanceParams.Sort((x, y) => String.Compare(x.Definition.Name, y.Definition.Name));
                                        foreach (Parameter p in typeParams)
                                        {
                                            RevitParameter rp = new RevitParameter();
                                            rp.ParameterName = p.Definition.Name;
                                            rp.StorageType = p.StorageType.ToString();
                                            rp.IsType = true;
                                            parameters.Add(rp);
                                        }
                                        foreach (Parameter p in instanceParams)
                                        {
                                            RevitParameter rp = new RevitParameter();
                                            rp.ParameterName = p.Definition.Name;
                                            rp.StorageType = p.StorageType.ToString();
                                            rp.IsType = false;
                                            parameters.Add(rp);
                                        }
                                    }
                                }
                                break;
                            }
                        }
                    }
                });
            }
            return parameters;
        }

        //public bool CreateObjects(List<RevitObject> objects)
        //{
        //    TaskContainer.Instance.EnqueueTask(uiApp =>
        //        {
        //            try
        //            {
        //                TaskDialog dlg = new TaskDialog("Warning");
        //                dlg.MainInstruction = "Incoming Data";
        //                dlg.MainContent = "Data is being sent to Revit from another application using Lyrebird." +
        //                    "  Do you want to accept the incoming data and create new elements?";
        //                dlg.CommonButtons = TaskDialogCommonButtons.Yes | TaskDialogCommonButtons.No;

        //                TaskDialogResult result = dlg.Show();
        //                if (result == TaskDialogResult.Yes)
        //                {
        //                    // Go ahead and create some new elements
        //                    //TaskDialog.Show("Temporary", "go ahead and create " + objects.Count.ToString() + " new elements");
        //                    Document doc = uiApp.ActiveUIDocument.Document;

        //                    // Find the FamilySymbol
        //                    FamilySymbol symbol = null;
        //                    FilteredElementCollector famCollector = new FilteredElementCollector(doc);
        //                    famCollector.OfClass(typeof(Family));
        //                    foreach (Family f in famCollector)
        //                    {
        //                        if (f.Name == objects[0].FamilyName)
        //                        {
        //                            foreach (FamilySymbol fs in f.Symbols)
        //                            {
        //                                if (fs.Name == objects[0].TypeName)
        //                                {
        //                                    symbol = fs;
        //                                }
        //                            }
        //                        }
        //                    }

        //                    if (symbol != null)
        //                    {
        //                        using (Transaction t = new Transaction(doc, "Create Objects"))
        //                        {
        //                            t.Start();
        //                            foreach (RevitObject ro in objects)
        //                            {
        //                                XYZ origin = new XYZ(ro.Origin.X, ro.Origin.Y, ro.Origin.Z);
        //                                FamilyInstance fi = doc.Create.NewFamilyInstance(origin, symbol, Autodesk.Revit.DB.Structure.StructuralType.NonStructural);
        //                                foreach (RevitParameter rp in ro.Parameters)
        //                                {
        //                                    try
        //                                    {
        //                                        Parameter p = fi.get_Parameter(rp.ParameterName);
        //                                        switch (rp.StorageType)
        //                                        {
        //                                            case "Double":
        //                                                p.Set(Convert.ToDouble(rp.Value));
        //                                                break;
        //                                            case "Integer":
        //                                                p.Set(Convert.ToInt32(rp.Value));
        //                                                break;
        //                                            case "String":
        //                                                p.Set(rp.Value);
        //                                                break;
        //                                            case "ElementId":
        //                                                p.Set(new ElementId(Convert.ToInt32(rp.Value)));
        //                                                break;
        //                                            default:
        //                                                p.Set(rp.Value);
        //                                                break;
        //                                        }
        //                                    }
        //                                    catch { }
        //                                }
        //                                // Rotate
        //                                if (ro.Orientation != null)
        //                                {
        //                                    if (ro.Orientation.Z == 0)
        //                                    {
        //                                        Line axis = Line.CreateBound(origin, origin + XYZ.BasisZ);
        //                                        double angle = Math.Atan2(ro.Orientation.Y, ro.Orientation.X);
        //                                        ElementTransformUtils.RotateElement(doc, fi.Id, axis, angle);
        //                                    }
        //                                }
        //                            }
        //                            t.Commit();
        //                        }
        //                    }
        //                    else
        //                    {
        //                        TaskDialog.Show("Error", "Could not find the desired family type");
        //                    }
        //                }
        //            }
        //            finally
        //            {
        //                Monitor.Pulse(_locker);
        //            }
        //        });
        //    Monitor.Wait(_locker, WAIT_TIMEOUT);
            
        //    return true;
        //}

        public bool CreateOrModify(List<RevitObject> incomingObjs, Guid uniqueId)
        {

            TaskContainer.Instance.EnqueueTask(uiApp =>
            {
                try
                {
                    // Find existing elements
                    List<ElementId> existing = FindExisting(uiApp.ActiveUIDocument.Document, uniqueId, incomingObjs[0].Category);
                    
                    TaskDialog dlg = new TaskDialog("Warning");
                    dlg.MainInstruction = "Incoming Data";

                    int option = 0;
                    if (existing == null || existing.Count == 0)
                    {
                        option = 0;
                        dlg.MainContent = "Data is being sent to Revit from another application using Lyrebird." +
                            " This data will be used to create " + incomingObjs.Count.ToString() + " elements.  How would you like to proceed?";
                        dlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink1, "Create new elements");
                        dlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink2, "Cancel");
                    }
                    else if (existing != null && existing.Count == incomingObjs.Count)
                    {
                        option = 1;
                        dlg.MainContent = "Data is being sent to Revit from another application using Lyrebird." +
                            " This incoming data matches up with " + incomingObjs.Count.ToString() + " elements.  How would you like to proceed?";
                        dlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink1, "Modify existing elements with incoming data");
                        dlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink2, "Create new elements, ignore all exisiting.");
                        dlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink3, "Cancel");
                    }
                    else if (existing != null && existing.Count < incomingObjs.Count)
                    {
                        option = 2;
                        dlg.MainContent = "Data is being sent to Revit from another application using Lyrebird." +
                            " This incoming data matches up with " + incomingObjs.Count.ToString() + " elements but includes " + (incomingObjs.Count - existing.Count).ToString() +
                            " additional elements.  How would you like to proceed?";
                        dlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink1, "Modify existing and create new elements with incoming data");
                        dlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink2, "Create new elements, ignore all exisiting.");
                        dlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink3, "Cancel");
                    }
                    else if (existing != null && existing.Count > incomingObjs.Count)
                    {
                        option = 3;
                        dlg.MainContent = "Data is being sent to Revit from another application using Lyrebird." +
                            " This incoming data matches up with " + incomingObjs.Count.ToString() + " elements but there are an additional " + (existing.Count - incomingObjs.Count).ToString() +
                            " in the model in addition to what's coming in.  How would you like to proceed?";
                        dlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink1, "Modify the first set of existing and delete additional elements to match incoming data");
                        dlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink2, "Modify the first set of existing objects and ignore any additional elements.");
                        dlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink3, "Create new elements, ignore all exisiting.");
                        dlg.AddCommandLink(TaskDialogCommandLinkId.CommandLink4, "Cancel");
                    }

                    TaskDialogResult result = dlg.Show();
                    if (result == TaskDialogResult.CommandLink1)
                    {
                        if (option == 0)
                        {
                            // Create new
                            try
                            {
                                CreateObjects(incomingObjs, uiApp.ActiveUIDocument.Document, uniqueId);
                            }
                            catch (Exception ex)
                            {
                                TaskDialog.Show("Error", ex.Message);
                            }
                        }
                        else if (option == 1)
                        {
                            // Modify
                            try
                            {
                                ModifyObjects(incomingObjs, existing, uiApp.ActiveUIDocument.Document);
                            }
                            catch { }
                        }
                        else if (option == 2)
                        {
                            // create and modify
                            List<RevitObject> existingObjects = new List<RevitObject>();
                            List<RevitObject> newObjects = new List<RevitObject>();

                            int i = 0;
                            while (i < existing.Count)
                            {
                                existingObjects.Add(incomingObjs[i]);
                                i++;
                            }
                            while (i < incomingObjs.Count)
                            {
                                newObjects.Add(incomingObjs[i]);
                                i++;
                            }
                            try
                            {
                                ModifyObjects(existingObjects, existing, uiApp.ActiveUIDocument.Document);
                                CreateObjects(newObjects, uiApp.ActiveUIDocument.Document, uniqueId);
                            }
                            catch { }
                        }
                        else if (option == 3)
                        {
                            // Modify and Delete
                            List<RevitObject> existingObjects = new List<RevitObject>();
                            List<ElementId> removeObjects = new List<ElementId>();

                            int i = 0;
                            while (i < incomingObjs.Count)
                            {
                                existingObjects.Add(incomingObjs[i]);
                                i++;
                            }
                            while (i < existing.Count)
                            {
                                removeObjects.Add(existing[i]);
                                i++;
                            }
                            try
                            {
                                ModifyObjects(existingObjects, existing, uiApp.ActiveUIDocument.Document);
                                DeleteExisting(uiApp.ActiveUIDocument.Document, removeObjects);
                            }
                            catch { }
                        }
                    }
                    else if (result == TaskDialogResult.CommandLink2)
                    {
                        // if 0, do nothing
                        if (option == 1)
                        {
                            // Create new
                            try
                            {
                                CreateObjects(incomingObjs, uiApp.ActiveUIDocument.Document, uniqueId);
                            }
                            catch { }
                        }
                        else if (option == 2)
                        {
                            // Create new
                            try
                            {
                                CreateObjects(incomingObjs, uiApp.ActiveUIDocument.Document, uniqueId);
                            }
                            catch { }
                        }
                        else if (option == 3)
                        {
                            // Modify and ignore
                            List<RevitObject> existingObjects = new List<RevitObject>();
                            List<ElementId> removeObjects = new List<ElementId>();

                            int i = 0;
                            while (i < incomingObjs.Count)
                            {
                                existingObjects.Add(incomingObjs[i]);
                                i++;
                            }
                            try
                            {
                                ModifyObjects(existingObjects, existing, uiApp.ActiveUIDocument.Document);
                            }
                            catch { }
                        }
                    }
                    else if (result == TaskDialogResult.CommandLink3)
                    {
                        if (option == 3)
                        {
                            // Create new
                            try
                            {
                                CreateObjects(incomingObjs, uiApp.ActiveUIDocument.Document, uniqueId);
                            }
                            catch { }
                        }
                        
                    }
                }
                catch
                {

                }
                finally
                {
                    Monitor.Pulse(_locker);
                }
            });
            Monitor.Wait(_locker, WAIT_TIMEOUT);
            
            return true;
        }

        public string GetDocumentName()
        {
            lock (_locker)
            {
                UIApplication uiapp = LMNA.Lyrebird.RevitServerApp.UIApp;
                string docName = "Not finished";
                try
                {
                    docName = uiapp.ActiveUIDocument.Document.Title;
                    currentDocName = docName;
                }
                catch
                {
                    currentDocName = "I FAILED";
                }
                Monitor.Wait(_locker, WAIT_TIMEOUT);
            }
            return currentDocName;
        }

        private void CreateObjects(List<RevitObject> revitObjects, Document doc, Guid uniqueId)
        {
            // Create new Revit objects.
            List<LyrebirdId> newUniqueIds = new List<LyrebirdId>();
            
            // Get the levels from the project
            FilteredElementCollector lvlCollector = new FilteredElementCollector(doc);
            lvlCollector.OfClass(typeof(Level)).ToElements().OfType<Level>();

            // Determine what kind of object we're creating.
            RevitObject ro = revitObjects[0];

            #region Normal Origin based Family Instance
            if (ro.Origin != null)
            {
                // Find the FamilySymbol
                FamilySymbol symbol = FindFamilySymbol(ro.FamilyName, ro.TypeName, doc);
                
                if (symbol != null)
                {
                    // Get the hosting ID from the family.
                    Family fam = symbol.Family;
                    Parameter hostParam = fam.get_Parameter(BuiltInParameter.FAMILY_HOSTING_BEHAVIOR);
                    int hostBehavior = hostParam.AsInteger();

                    using (Transaction t = new Transaction(doc, "Lyrebird Create Objects"))
                    {
                        t.Start();
                        try
                        {
                            // Create the Schema for the instances to store the GH Component InstanceGUID and the path
                            Schema instanceSchema = null;
                            try
                            {
                                instanceSchema = Schema.Lookup(instanceSchemaGUID);
                            }
                            catch { }
                            if (instanceSchema == null)
                            {
                                SchemaBuilder sb = new SchemaBuilder(instanceSchemaGUID);
                                sb.SetWriteAccessLevel(AccessLevel.Vendor);
                                sb.SetReadAccessLevel(AccessLevel.Public);
                                sb.SetVendorId("LMNA");

                                // Create the field to store the data in the family
                                FieldBuilder guidFB = sb.AddSimpleField("InstanceID", typeof(string));
                                guidFB.SetDocumentation("Component instance GUID from Grasshopper");

                                sb.SetSchemaName("LMNtsInstanceGUID");
                                instanceSchema = sb.Finish();
                            }
                            FamilyInstance fi = null;
                            XYZ origin = XYZ.Zero;
                            if (hostBehavior == 0)
                            {
                                foreach (RevitObject obj in revitObjects)
                                {
                                    try
                                    {
                                        origin = new XYZ(obj.Origin.X, obj.Origin.Y, obj.Origin.Z);
                                        fi = doc.Create.NewFamilyInstance(origin, symbol, Autodesk.Revit.DB.Structure.StructuralType.NonStructural);
                                    }
                                    catch (Exception ex)
                                    {
                                        TaskDialog.Show("Error", ex.Message);
                                    }
                                    // Rotate
                                    if (ro.Orientation != null)
                                    {
                                        if (ro.Orientation.Z == 0)
                                        {
                                            Line axis = Line.CreateBound(origin, origin + XYZ.BasisZ);
                                            double angle = Math.Atan2(ro.Orientation.Y, ro.Orientation.X);
                                            ElementTransformUtils.RotateElement(doc, fi.Id, axis, angle);
                                        }
                                    }

                                    // Assign the parameters
                                    SetParameters(fi, obj.Parameters);

                                    // Assign the GH InstanceGuid
                                    AssignGuid(fi, uniqueId, instanceSchema);
                                }
                            }
                            else
                            {
                                foreach (RevitObject obj in revitObjects)
                                {
                                    origin = new XYZ(obj.Origin.X, obj.Origin.Y, obj.Origin.Z);
                                    
                                    // Find the level
                                    List<LyrebirdPoint> lbPoints = new List<LyrebirdPoint>();
                                    lbPoints.Add(obj.Origin);
                                    Level lvl = GetLevel(lbPoints, doc);

                                    // Get the host
                                    if (hostBehavior == 5)
                                    {
                                        // Face based family.  Find the face and create the element
                                        XYZ normVector = new XYZ(obj.Orientation.X, obj.Orientation.Y, obj.Orientation.Z);
                                        XYZ faceVector;
                                        if (obj.FaceOrientation != null)
                                        {
                                            faceVector = new XYZ(obj.FaceOrientation.X, obj.FaceOrientation.Y, obj.FaceOrientation.Z);
                                        }
                                        else
                                        {
                                            faceVector = XYZ.BasisZ;
                                        }
                                        Face face = FindFace(origin, normVector, doc);
                                        if (face != null)
                                        {
                                            fi = doc.Create.NewFamilyInstance(face, origin, faceVector, symbol);
                                        }
                                    }
                                    else
                                    {
                                        // typical hosted family.  Can be wall, floor, roof or ceiling.
                                        ElementId host = FindHost(origin, hostBehavior, doc);
                                        if (host != null)
                                        {
                                            fi = doc.Create.NewFamilyInstance(origin, symbol, doc.GetElement(host), lvl, Autodesk.Revit.DB.Structure.StructuralType.NonStructural);
                                        }
                                    }
                                    // Assign the parameters
                                    SetParameters(fi, obj.Parameters);

                                    // Assign the GH InstanceGuid
                                    AssignGuid(fi, uniqueId, instanceSchema);
                                }
                                // delete the host finder
                                ElementId hostFinderFamily = hostFinder.Symbol.Family.Id;
                                doc.Delete(hostFinder.Id);
                                doc.Delete(hostFinderFamily);
                                hostFinder = null;
                            }
                        }
                        catch (Exception ex)
                        {
                            TaskDialog.Show("Error", ex.Message);
                        }
                        t.Commit();
                    }
                }
                else
                {
                    TaskDialog.Show("Error", "Could not find the desired family type");
                }

            }
            #endregion

            #region Adaptive Components
            else if (ro.AdaptivePoints != null && ro.AdaptivePoints.Count > 0)
            {
                // Find the FamilySymbol
                FamilySymbol symbol = FindFamilySymbol(ro.FamilyName, ro.TypeName, doc);

                if (symbol != null)
                {
                    using (Transaction t = new Transaction(doc, "Lyrebird Create Objects"))
                    {
                        t.Start();
                        try
                        {
                            // Create the Schema for the instances to store the GH Component InstanceGUID and the path
                            Schema instanceSchema = null;
                            try
                            {
                                instanceSchema = Schema.Lookup(instanceSchemaGUID);
                            }
                            catch { }
                            if (instanceSchema == null)
                            {
                                SchemaBuilder sb = new SchemaBuilder(instanceSchemaGUID);
                                sb.SetWriteAccessLevel(AccessLevel.Vendor);
                                sb.SetReadAccessLevel(AccessLevel.Public);
                                sb.SetVendorId("LMNA");

                                // Create the field to store the data in the family
                                FieldBuilder guidFB = sb.AddSimpleField("InstanceID", typeof(string));
                                guidFB.SetDocumentation("Component instance GUID from Grasshopper");

                                sb.SetSchemaName("LMNtsInstanceGUID");
                                instanceSchema = sb.Finish();
                            }
                            FamilyInstance fi = null;
                            try
                            {
                                foreach (RevitObject obj in revitObjects)
                                {
                                    fi = AdaptiveComponentInstanceUtils.CreateAdaptiveComponentInstance(doc, symbol);
                                    IList<ElementId> placePointIds = new List<ElementId>();
                                    placePointIds = AdaptiveComponentInstanceUtils.GetInstancePlacementPointElementRefIds(fi);
                                    
                                    for (int ptNum = 0; ptNum < obj.AdaptivePoints.Count; ptNum++)
                                    {
                                        try
                                        {
                                            ReferencePoint rp = doc.GetElement(placePointIds[ptNum]) as ReferencePoint;
                                            XYZ pt = new XYZ(obj.AdaptivePoints[ptNum].X, obj.AdaptivePoints[ptNum].Y, obj.AdaptivePoints[ptNum].Z);
                                            XYZ vector = pt.Subtract(rp.Position);
                                            ElementTransformUtils.MoveElement(doc, rp.Id, vector);
                                        }
                                        catch { }
                                    }

                                    // Assign the parameters
                                    SetParameters(fi, obj.Parameters);

                                    // Assign the GH InstanceGuid
                                    AssignGuid(fi, uniqueId, instanceSchema);
                                }
                                
                            }
                            catch { }
                        }
                        catch { }
                        t.Commit();
                    }
                }
            }
            #endregion

            #region Curve based
            else if (ro.Curves != null && ro.Curves.Count > 0)
            {
                
                // Find the FamilySymbol
                FamilySymbol symbol = null;
                WallType wallType = null;
                FloorType floorType = null;
                RoofType roofType = null;
                bool typeFound = false;

                FilteredElementCollector famCollector = new FilteredElementCollector(doc);
                
                if (ro.Category == "Walls")
                {
                    famCollector.OfClass(typeof(WallType));
                    foreach (WallType wt in famCollector)
                    {
                        if (wt.Name == ro.TypeName)
                        {
                            wallType = wt;
                            typeFound = true;
                            break;
                        }
                    }
                }
                else if (ro.Category == "Floors")
                {
                    famCollector.OfClass(typeof(FloorType));
                    foreach (FloorType ft in famCollector)
                    {
                        if (ft.Name == ro.TypeName)
                        {
                            floorType = ft;
                            typeFound = true;
                            break;
                        }
                    }
                }
                else if (ro.Category == "Roofs")
                {
                    famCollector.OfClass(typeof(RoofType));
                    foreach (RoofType rt in famCollector)
                    {
                        if (rt.Name == ro.TypeName)
                        {
                            roofType = rt;
                            typeFound = true;
                            break;
                        }
                    }
                }
                else
                {
                    symbol = FindFamilySymbol(ro.FamilyName, ro.TypeName, doc);
                    if (symbol != null)
                        typeFound = true;
                }


                
                if (typeFound)
                {
                    using (Transaction t = new Transaction(doc, "Lyrebird Create Objects"))
                    {
                        
                        t.Start();
                        try
                        {
                            // Create the Schema for the instances to store the GH Component InstanceGUID and the path
                            Schema instanceSchema = null;
                            try
                            {
                                instanceSchema = Schema.Lookup(instanceSchemaGUID);
                            }
                            catch { }
                            if (instanceSchema == null)
                            {
                                SchemaBuilder sb = new SchemaBuilder(instanceSchemaGUID);
                                sb.SetWriteAccessLevel(AccessLevel.Vendor);
                                sb.SetReadAccessLevel(AccessLevel.Public);
                                sb.SetVendorId("LMNA");

                                // Create the field to store the data in the family
                                FieldBuilder guidFB = sb.AddSimpleField("InstanceID", typeof(string));
                                guidFB.SetDocumentation("Component instance GUID from Grasshopper");

                                sb.SetSchemaName("LMNtsInstanceGUID");
                                instanceSchema = sb.Finish();
                            }
                            FamilyInstance fi = null;
                            try
                            {
                                
                                foreach (RevitObject obj in revitObjects)
                                {

                                    #region single line based family
                                    if (obj.Curves.Count == 1)
                                    {
                                            
                                        LyrebirdCurve lbc = obj.Curves[0];
                                        List<LyrebirdPoint> curvePoints = lbc.ControlPoints.OrderBy(p => p.Z).ToList();
                                        // linear
                                        // can be a wall or line based family.
                                        if (obj.Category == "Walls")
                                        {
                                                
                                            // draw a wall
                                            Curve crv = null;
                                            if (lbc.CurveType == "Line")
                                            {
                                                XYZ pt1 = new XYZ(lbc.ControlPoints[0].X, lbc.ControlPoints[0].Y, lbc.ControlPoints[0].Z);
                                                XYZ pt2 = new XYZ(lbc.ControlPoints[1].X, lbc.ControlPoints[1].Y, lbc.ControlPoints[1].Z);
                                                crv = Line.CreateBound(pt1, pt2);
                                            }
                                            else if (lbc.CurveType == "Arc")
                                            {
                                                XYZ pt1 = new XYZ(lbc.ControlPoints[0].X, lbc.ControlPoints[0].Y, lbc.ControlPoints[0].Z);
                                                XYZ pt2 = new XYZ(lbc.ControlPoints[1].X, lbc.ControlPoints[1].Y, lbc.ControlPoints[1].Z);
                                                XYZ pt3 = new XYZ(lbc.ControlPoints[2].X, lbc.ControlPoints[2].Y, lbc.ControlPoints[2].Z);
                                                crv = Arc.Create(pt1, pt3, pt2);
                                            }

                                            if (crv != null)
                                            {
                                                    
                                                // Find the level
                                                Level lvl = GetLevel(lbc.ControlPoints, doc);
                                                
                                                double offset = 0;
                                                if (curvePoints[0].Z != lvl.Elevation)
                                                {
                                                    offset = lvl.Elevation - curvePoints[0].Z;
                                                }
                                                    
                                                // Create the wall
                                                Wall w = null;
                                                try
                                                {
                                                    w = Wall.Create(doc, crv, wallType.Id, lvl.Id, 10.0, offset, false, false);
                                                }
                                                catch (Exception ex)
                                                {
                                                    TaskDialog.Show("ERROR", ex.Message);
                                                }
                                                
                                                // Assign the parameters
                                                SetParameters(w, obj.Parameters);

                                                // Assign the GH InstanceGuid
                                                AssignGuid(w, uniqueId, instanceSchema);
                                            }
                                        }
                                        // See if it's a structural column
                                        else if (obj.Category == "Structural Columns")
                                        {
                                            if (symbol != null && lbc.CurveType == "Line")
                                            {
                                                Curve crv = null;
                                                XYZ origin = new XYZ(lbc.ControlPoints[0].X, lbc.ControlPoints[0].Y, lbc.ControlPoints[0].Z);
                                                XYZ pt2 = new XYZ(lbc.ControlPoints[1].X, lbc.ControlPoints[1].Y, lbc.ControlPoints[1].Z);
                                                crv = Line.CreateBound(origin, pt2);

                                                // Find the level
                                                Level lvl = GetLevel(lbc.ControlPoints, doc);
                                                
                                                // Create the column
                                                fi = doc.Create.NewFamilyInstance(origin, symbol, lvl, Autodesk.Revit.DB.Structure.StructuralType.Column);

                                                // Change it to a slanted column
                                                Parameter slantParam = fi.get_Parameter(BuiltInParameter.SLANTED_COLUMN_TYPE_PARAM);

                                                // SlantedOrVerticalColumnType has 3 options, CT_Vertical (0), CT_Angle (1), or CT_EndPoint (2)
                                                // CT_EndPoint is what we want for a line based column.
                                                slantParam.Set(2);

                                                // Set the location curve of the column to the line
                                                LocationCurve lc = fi.Location as LocationCurve;
                                                if (lc != null)
                                                {
                                                    lc.Curve = crv;
                                                }

                                                // Assign the parameters
                                                SetParameters(fi, obj.Parameters);

                                                // Assign the GH InstanceGuid
                                                AssignGuid(fi, uniqueId, instanceSchema);
                                            }
                                        }

                                        // Otherwise create a family it using the line
                                        else
                                        {
                                            if (symbol != null)
                                            {
                                                Curve crv = null;

                                                if (lbc.CurveType == "Line")
                                                {
                                                    XYZ origin = new XYZ(lbc.ControlPoints[0].X, lbc.ControlPoints[0].Y, lbc.ControlPoints[0].Z);
                                                    XYZ pt2 = new XYZ(lbc.ControlPoints[1].X, lbc.ControlPoints[1].Y, lbc.ControlPoints[1].Z);
                                                    crv = Line.CreateBound(origin, pt2);
                                                }
                                                else if (lbc.CurveType == "Arc")
                                                {
                                                    XYZ pt1 = new XYZ(lbc.ControlPoints[0].X, lbc.ControlPoints[0].Y, lbc.ControlPoints[0].Z);
                                                    XYZ pt2 = new XYZ(lbc.ControlPoints[1].X, lbc.ControlPoints[1].Y, lbc.ControlPoints[1].Z);
                                                    XYZ pt3 = new XYZ(lbc.ControlPoints[2].X, lbc.ControlPoints[2].Y, lbc.ControlPoints[2].Z);
                                                    crv = Arc.Create(pt1, pt3, pt2);
                                                }
                                                // Find the level
                                                Level lvl = GetLevel(lbc.ControlPoints, doc);
                                                double offset = 0;

                                                // Create the family
                                                if (symbol.Category.Name == "Detail Items")
                                                {
                                                    try
                                                    {
                                                        Line line = crv as Line;
                                                        fi = doc.Create.NewFamilyInstance(line, symbol, doc.ActiveView);
                                                    }
                                                    catch { }
                                                }
                                                else if(symbol.Category.Name == "Structural Framing")
                                                {
                                                    try
                                                    {
                                                        if (lbc.CurveType == "Arc")
                                                        {
                                                            XYZ origin = new XYZ(lbc.ControlPoints[0].X, lbc.ControlPoints[0].Y, lbc.ControlPoints[0].Z);
                                                            fi = doc.Create.NewFamilyInstance(origin, symbol, lvl, Autodesk.Revit.DB.Structure.StructuralType.Beam);
                                                            // Set the location curve of the column to the line
                                                            LocationCurve lc = fi.Location as LocationCurve;
                                                            if (lc != null)
                                                            {
                                                                lc.Curve = crv;
                                                            }
                                                        }
                                                        else
                                                        {
                                                            fi = doc.Create.NewFamilyInstance(crv, symbol, lvl, Autodesk.Revit.DB.Structure.StructuralType.Beam);
                                                        }
                                                    }
                                                    catch { }
                                                }
                                                else
                                                {
                                                    try
                                                    {
                                                        fi = doc.Create.NewFamilyInstance(crv, symbol, lvl, Autodesk.Revit.DB.Structure.StructuralType.NonStructural);
                                                    }
                                                    catch { }
                                                }

                                                // Assign the parameters
                                                SetParameters(fi, obj.Parameters);

                                                // Assign the GH InstanceGuid
                                                AssignGuid(fi, uniqueId, instanceSchema);
                                            }
                                        }
                                    }
                                    #endregion

                                    #region Closed Curve Family
                                    else
                                    {
                                        // A list of curves.  These should equate a closed planar curve from GH.
                                        
                                        //TODO: For each profile type, determine if the offset is working correctly or inverted

                                        // Then determine category and create based on that.
                                        if (obj.Category == "Walls")
                                        {
                                            // Create line based wall
                                            // Find the level
                                            Level lvl = null;
                                            double offset = 0;
                                            List<LyrebirdPoint> allPoints = new List<LyrebirdPoint>();
                                            foreach (LyrebirdCurve lc in obj.Curves)
                                            {
                                                foreach (LyrebirdPoint lp in lc.ControlPoints)
                                                {
                                                    allPoints.Add(lp);
                                                }
                                            }
                                            allPoints.Sort((x, y) => x.Z.CompareTo(y.Z));

                                            lvl = GetLevel(allPoints, doc);
                                            
                                            if (allPoints[0].Z != lvl.Elevation)
                                            {
                                                offset = allPoints[0].Z - lvl.Elevation;
                                            }

                                            // Generate the curvearray from the incoming curves
                                            List<Curve> crvArray = new List<Curve>();
                                            try
                                            {
                                                for (int i = 0; i < obj.Curves.Count; i++)
                                                {
                                                    LyrebirdCurve lbc = obj.Curves[i];
                                                    if (lbc.CurveType == "Arc")
                                                    {
                                                        XYZ pt1 = new XYZ(lbc.ControlPoints[0].X, lbc.ControlPoints[0].Y, lbc.ControlPoints[0].Z);
                                                        XYZ pt2 = new XYZ(lbc.ControlPoints[1].X, lbc.ControlPoints[1].Y, lbc.ControlPoints[1].Z);
                                                        XYZ pt3 = new XYZ(lbc.ControlPoints[2].X, lbc.ControlPoints[2].Y, lbc.ControlPoints[2].Z);
                                                        Arc arc = Arc.Create(pt1, pt3, pt2);
                                                        crvArray.Add(arc);
                                                    }
                                                    else if (lbc.CurveType == "Line")
                                                    {
                                                        XYZ pt1 = new XYZ(lbc.ControlPoints[0].X, lbc.ControlPoints[0].Y, lbc.ControlPoints[0].Z);
                                                        XYZ pt2 = new XYZ(lbc.ControlPoints[1].X, lbc.ControlPoints[1].Y, lbc.ControlPoints[1].Z);
                                                        Line line = Line.CreateBound(pt1, pt2);
                                                        crvArray.Add(line);
                                                    }
                                                    else if (lbc.CurveType == "Spline")
                                                    {
                                                        List<XYZ> controlPoints = new List<XYZ>();
                                                        List<double> weights = lbc.Weights;
                                                        List<double> knots = lbc.Knots;

                                                        foreach (LyrebirdPoint lp in lbc.ControlPoints)
                                                        {
                                                            XYZ pt = new XYZ(lp.X, lp.Y, lp.Z);
                                                            controlPoints.Add(pt);
                                                        }
                                                        NurbSpline spline;
                                                        if (lbc.Degree < 3)
                                                        {
                                                            spline = NurbSpline.Create(controlPoints, weights);
                                                        }
                                                        else
                                                        {
                                                            spline = NurbSpline.Create(controlPoints, weights, knots, lbc.Degree, false, true);
                                                        }
                                                        crvArray.Add(spline);
                                                    }
                                                }
                                            }
                                            catch (Exception ex)
                                            {
                                                TaskDialog.Show("ERROR", ex.Message);
                                            }

                                            // Create the floor
                                            Wall w = null;
                                            w = Wall.Create(doc, crvArray, wallType.Id, lvl.Id, false);
                                            if (offset != 0)
                                            {
                                                Parameter p = w.get_Parameter(BuiltInParameter.WALL_BASE_OFFSET);
                                                p.Set(offset);
                                            }

                                            // Assign the parameters
                                            SetParameters(w, obj.Parameters);

                                            // Assign the GH InstanceGuid
                                            AssignGuid(w, uniqueId, instanceSchema);

                                        }
                                        else if (obj.Category == "Floors")
                                        {
                                            // Create a profile based floor
                                            // Find the level
                                            Level lvl = GetLevel(obj.Curves[0].ControlPoints, doc);
                                            
                                            double offset = 0;
                                            if (obj.Curves[0].ControlPoints[0].Z != lvl.Elevation)
                                            {
                                                offset = obj.Curves[0].ControlPoints[0].Z - lvl.Elevation;
                                            }
                                            
                                            // Generate the curvearray from the incoming curves
                                            CurveArray crvArray = GetCurveArray(obj.Curves);
                                            
                                            // Create the floor
                                            Floor flr = null;
                                            flr = doc.Create.NewFloor(crvArray, floorType, lvl, false);
                                            
                                            if (offset != 0)
                                            {
                                                Parameter p = flr.get_Parameter(BuiltInParameter.FLOOR_HEIGHTABOVELEVEL_PARAM);
                                                p.Set(offset);
                                            }

                                            // Assign the parameters
                                            SetParameters(flr, obj.Parameters);

                                            // Assign the GH InstanceGuid
                                            AssignGuid(flr, uniqueId, instanceSchema);
                                            
                                        }
                                        else if (obj.Category == "Roofs")
                                        {
                                            // Create a RoofExtrusion
                                            // Find the level
                                            Level lvl = GetLevel(obj.Curves[0].ControlPoints, doc);
                                            
                                            double offset = 0;
                                            if (obj.Curves[0].ControlPoints[0].Z != lvl.Elevation)
                                            {
                                                offset = obj.Curves[0].ControlPoints[0].Z - lvl.Elevation;
                                            }
                                            
                                            // Generate the curvearray from the incoming curves
                                            CurveArray crvArray = GetCurveArray(obj.Curves);
                                            
                                            // Create the roof
                                            FootPrintRoof roof = null;
                                            ModelCurveArray roofProfile = new ModelCurveArray();
                                            try
                                            {
                                                roof = doc.Create.NewFootPrintRoof(crvArray, lvl, roofType, out roofProfile);
                                            }
                                            catch (Exception ex)
                                            {
                                                TaskDialog.Show("ERROR", ex.Message);
                                            }
                                            if (offset != 0)
                                            {
                                                Parameter p = roof.get_Parameter(BuiltInParameter.ROOF_LEVEL_OFFSET_PARAM);
                                                p.Set(offset);
                                            }

                                            // Assign the parameters
                                            SetParameters(roof, obj.Parameters);
                                            
                                            // Assign the GH InstanceGuid
                                            AssignGuid(roof, uniqueId, instanceSchema);
                                        }
                                    }
                                    #endregion
                                }
                            }
                            catch
                            {
                                
                            }
                        }
                        catch { }
                        t.Commit();
                    }
                }


            }
            #endregion
        }

        private bool ModifyObjects(List<RevitObject> existingObjects, List<ElementId> existingElems, Document doc)
        {
            bool succeeded = true;

            // TODO: Modify the existingElems according to data passed from existingObjects

            return succeeded;
        }

        private List<ElementId> FindExisting(Document doc, Guid uniqueId, string category)
        {
            // Find existing elements with a matching GUID from the GH component.
            List<ElementId> existingElems = new List<ElementId>();

            Schema instanceSchema = Schema.Lookup(instanceSchemaGUID);
            if (instanceSchema == null)
            {
                return existingElems;
            }

            // find the existing elements
            if (category == "Walls")
            {
                FilteredElementCollector collector = new FilteredElementCollector(doc);
                collector.OfCategory(BuiltInCategory.OST_Walls);
                collector.OfClass(typeof(Wall));
                foreach (Wall w in collector)
                {
                    try
                    {
                        Entity entity = w.GetEntity(instanceSchema);
                        if (entity.IsValid())
                        {
                            Field f = instanceSchema.GetField("InstanceID");
                            string tempId = entity.Get<string>(f);
                            if (tempId == uniqueId.ToString())
                            {
                                existingElems.Add(w.Id);
                            }
                        }
                    }
                    catch { }
                }
            }
            else if (category == "Floors")
            {
                FilteredElementCollector collector = new FilteredElementCollector(doc);
                collector.OfCategory(BuiltInCategory.OST_Floors);
                collector.OfClass(typeof(Floor));
                foreach (Floor flr in collector)
                {
                    try
                    {
                        Entity entity = flr.GetEntity(instanceSchema);
                        if (entity.IsValid())
                        {
                            Field f = instanceSchema.GetField("InstanceID");
                            string tempId = entity.Get<string>(f);
                            if (tempId == uniqueId.ToString())
                            {
                                existingElems.Add(flr.Id);
                            }
                        }
                    }
                    catch { }
                }
            }
            else if (category == "Roofs")
            {
                FilteredElementCollector collector = new FilteredElementCollector(doc);
                collector.OfCategory(BuiltInCategory.OST_Roofs);
                collector.OfClass(typeof(ExtrusionRoof));
                foreach (ExtrusionRoof r in collector)
                {
                    try
                    {
                        Entity entity = r.GetEntity(instanceSchema);
                        if (entity.IsValid())
                        {
                            Field f = instanceSchema.GetField("InstanceID");
                            string tempId = entity.Get<string>(f);
                            if (tempId == uniqueId.ToString())
                            {
                                existingElems.Add(r.Id);
                            }
                        }
                    }
                    catch { }
                }
            }
            else
            {
                FilteredElementCollector collector = new FilteredElementCollector(doc);
                collector.OfClass(typeof(FamilyInstance));
                
                foreach (Element e in collector)
                {
                    try
                    {
                        FamilyInstance fi = e as FamilyInstance;
                        Entity entity = fi.GetEntity(instanceSchema);
                        if (entity.IsValid())
                        {
                            Field f = instanceSchema.GetField("InstanceID");
                            string tempId = entity.Get<string>(f);
                            if (tempId == uniqueId.ToString())
                            {
                                existingElems.Add(e.Id);
                            }
                        }
                    }
                    catch { }
                }
            }

            return existingElems;
        }

        private void DeleteExisting(Document doc, List<ElementId> elements)
        {
            using (Transaction t = new Transaction(doc, "Lyrebird Delete Existing"))
            {
                t.Start();
                doc.Delete(elements);
                t.Commit();
            }
        }

        private FamilySymbol FindFamilySymbol(string familyName, string typeName, Document doc)
        {
            FilteredElementCollector famCollector = new FilteredElementCollector(doc);
            famCollector.OfClass(typeof(Family));
            
            FamilySymbol symbol = null;
            foreach (Family f in famCollector)
            {
                if (f.Name == familyName)
                {
                    foreach (FamilySymbol fs in f.Symbols)
                    {
                        if (fs.Name == typeName)
                        {
                            symbol = fs;
                            return symbol;
                        }
                    }
                }
            }
            return null;
        }

        private CurveArray GetCurveArray(List<LyrebirdCurve> curves)
        {
            CurveArray crvArray = new CurveArray();

            for (int i = 0; i < curves.Count; i++)
            {
                LyrebirdCurve lbc = curves[i];
                if (lbc.CurveType == "Arc")
                {
                    XYZ pt1 = new XYZ(lbc.ControlPoints[0].X, lbc.ControlPoints[0].Y, lbc.ControlPoints[0].Z);
                    XYZ pt2 = new XYZ(lbc.ControlPoints[1].X, lbc.ControlPoints[1].Y, lbc.ControlPoints[1].Z);
                    XYZ pt3 = new XYZ(lbc.ControlPoints[2].X, lbc.ControlPoints[2].Y, lbc.ControlPoints[2].Z);
                    Arc arc = Arc.Create(pt1, pt3, pt2);
                    crvArray.Append(arc);
                }
                else if (lbc.CurveType == "Line")
                {
                    XYZ pt1 = new XYZ(lbc.ControlPoints[0].X, lbc.ControlPoints[0].Y, lbc.ControlPoints[0].Z);
                    XYZ pt2 = new XYZ(lbc.ControlPoints[1].X, lbc.ControlPoints[1].Y, lbc.ControlPoints[1].Z);
                    Line line = Line.CreateBound(pt1, pt2);
                    crvArray.Append(line);
                }
                else if (lbc.CurveType == "Spline")
                {
                    List<XYZ> controlPoints = new List<XYZ>();
                    List<double> weights = lbc.Weights;
                    List<double> knots = lbc.Knots;

                    foreach (LyrebirdPoint lp in lbc.ControlPoints)
                    {
                        XYZ pt = new XYZ(lp.X, lp.Y, lp.Z);
                        controlPoints.Add(pt);
                    }
                                                        
                    if (lbc.Degree < 3)
                    {
                        HermiteSpline spline = HermiteSpline.Create(controlPoints, false);
                        crvArray.Append(spline);
                    }
                    else
                    {
                        NurbSpline spline = NurbSpline.Create(controlPoints, weights, knots, lbc.Degree, false, true);
                        crvArray.Append(spline);
                    }
                }
            }

            return crvArray;
        }

        private Level GetLevel(List<LyrebirdPoint> controlPoints, Document doc)
        {
            Level lvl = null;

            FilteredElementCollector lvlCollector = new FilteredElementCollector(doc);
            lvlCollector.OfClass(typeof(Level));
            foreach (Level l in lvlCollector)
            {
                if (lvl == null)
                {
                    lvl = l;
                }
                else
                {
                    if (Math.Abs(l.Elevation - controlPoints[0].Z) < Math.Abs(lvl.Elevation - controlPoints[0].Z))
                    {
                        lvl = l;
                    }
                }
            }

            return lvl;
        }

        private ElementId FindHost(XYZ location, int hostType, Document doc)
        {
            ElementId host = null;
            FilteredElementCollector collector = new FilteredElementCollector(doc);
            
            // Subtransaction to insert a family and use it to check for intersctions.
            // The family is then moved around to check for the host of each new object being created
            // After the element creation process is over the object and it's parent family are deleted form the project.
            using (SubTransaction subTrans = new SubTransaction(doc))
            {
                subTrans.Start();
                Family insertPoint = null;
                
                if(hostFinder == null)
                {
                    // check if the point family exists
                    string path = typeof(LyrebirdService).Assembly.Location.Replace("LMNA.Lyrebird.RevitServer.dll", "IntersectionPoint.rfa");
                    if (!System.IO.File.Exists(path))
                    {
                        // save the file from this assembly and load it into project
                        string directory = System.IO.Path.GetDirectoryName(path);
                        System.Reflection.Assembly assembly = System.Reflection.Assembly.GetExecutingAssembly();
                        WriteResource(assembly, "IntersectionPoint.rfa", path);
                    }
                
                    // Load the family and place an instance of it.
                    doc.LoadFamily(path, out insertPoint);
                    System.IO.File.Delete(path);
                    FamilySymbol ips = null;
                    foreach(FamilySymbol fs in insertPoint.Symbols)
                    {
                        ips = fs;
                    }
                    
                    // Create an instance
                    hostFinder = AdaptiveComponentInstanceUtils.CreateAdaptiveComponentInstance(doc, ips);
                }

                IList<ElementId> placePointIds = new List<ElementId>();
                placePointIds = AdaptiveComponentInstanceUtils.GetInstancePlacementPointElementRefIds(hostFinder);
                try
                {
                    ReferencePoint rp = doc.GetElement(placePointIds[0]) as ReferencePoint;
                    XYZ movedPt;
                    if (hostType == 1 || hostType == 3)
                    {
                        movedPt = new XYZ(location.X, location.Y, location.Z + 0.1);
                    }
                    else
                    {
                        movedPt = new XYZ(location.X, location.Y, location.Z - 0.1);
                    }
                    XYZ vector = movedPt.Subtract(rp.Position);
                    ElementTransformUtils.MoveElement(doc, rp.Id, vector);
                }
                catch { }
                
                Element elem = hostFinder as Element;
                if (elem != null)
                {
                    // Find the host element
                    if (hostType == 1)
                    {
                        // find a wall
                        collector.OfCategory(BuiltInCategory.OST_Walls);
                        collector.OfClass(typeof(Wall));

                        ElementIntersectsElementFilter intersectionFilter = new ElementIntersectsElementFilter(elem);
                        collector.WherePasses(intersectionFilter);
                        foreach (Element e in collector)
                        {
                            host = e.Id;
                        }

                    }
                    else if (hostType == 2)
                    {
                        // Find a floor
                        collector.OfCategory(BuiltInCategory.OST_Floors);
                        collector.OfClass(typeof(Floor));

                        ElementIntersectsElementFilter intersectionFilter = new ElementIntersectsElementFilter(elem);
                        collector.WherePasses(intersectionFilter);

                        foreach (Element e in collector)
                        {
                            host = e.Id;
                        }
                    }
                    else if (hostType == 3)
                    {
                        // find a ceiling
                        collector.OfCategory(BuiltInCategory.OST_Ceilings);
                        collector.OfClass(typeof(Ceiling));

                        ElementIntersectsElementFilter intersectionFilter = new ElementIntersectsElementFilter(elem);
                        collector.WherePasses(intersectionFilter);

                        foreach (Element e in collector)
                        {
                            host = e.Id;
                        }
                    }
                    else if (hostType == 4)
                    {
                        // find a roof
                        collector.OfCategory(BuiltInCategory.OST_Roofs);
                        collector.OfClass(typeof(RoofBase));

                        ElementIntersectsElementFilter intersectionFilter = new ElementIntersectsElementFilter(elem);
                        collector.WherePasses(intersectionFilter);

                        foreach (Element e in collector)
                        {
                            host = e.Id;
                        }
                    }
                }
                subTrans.Commit();

                // Delete the family file
                
            }

            return host;
        }


        private Face FindFace(XYZ location, XYZ vector, Document doc)
        {
            Face face = null;

            bool found = false;
            
            FilteredElementCollector elementCollector = new FilteredElementCollector(doc);
            elementCollector.WhereElementIsNotElementType();

            Options opt = new Options();
            opt.ComputeReferences = true;
            foreach (Element e in elementCollector)
            {
                try
                {
                    GeometryElement ge = e.get_Geometry(opt);
                    foreach (GeometryObject go in ge)
                    {
                        try
                        {
                            XYZ endPt = location + vector;
                            Line l = Line.CreateBound(location - vector, endPt);
                            Curve c = l as Curve;
                            double dist = 1000000;
                            GeometryInstance geoInst = go as GeometryInstance;
                            if (geoInst != null)
                            {
                                // Family instance
                                GeometryElement instGeometry = geoInst.GetInstanceGeometry();
                                foreach (GeometryObject o in instGeometry)
                                {
                                    Solid s = o as Solid;
                                    foreach (Face f in s.Faces)
                                    {
                                        IntersectionResultArray results;
                                        SetComparisonResult result = f.Intersect(c, out results);
                                        if (results != null)
                                        {
                                            foreach (IntersectionResult res in results)
                                            {
                                                XYZ intersect = res.XYZPoint;
                                                double tempDist = location.DistanceTo(intersect);
                                                if (tempDist < dist)
                                                {
                                                    dist = tempDist;
                                                    face = f;
                                                    if (dist < 0.05)
                                                    {
                                                        return f;
                                                    }
                                                }
                                            }
                                        }
                                    }
                                }
                            }
                            else
                            {
                                // Assume something like a wall.
                                Solid solid = go as Solid;
                                
                                foreach (Face f in solid.Faces)
                                {
                                    IntersectionResultArray results;
                                    SetComparisonResult result = f.Intersect(c, out results);
                                    if (results != null)
                                    {
                                        foreach (IntersectionResult res in results)
                                        {
                                            XYZ intersect = res.XYZPoint;
                                            double tempDist = location.DistanceTo(intersect);
                                            if (tempDist < dist)
                                            {
                                                dist = tempDist;
                                                face = f;
                                                if (dist < 0.05)
                                                {
                                                    return f;
                                                }
                                            }
                                        }
                                    }
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            //errors++;
                        }
                    }
                }
                catch
                {
                    //errors++;
                }
            }
            if (face == null)
            {
                TaskDialog.Show("error", "no face found");
            }
            return face;
        }

        public static void WriteResource(System.Reflection.Assembly targetAssembly, string resourceName, string filePath)
        {
            string[] resources = targetAssembly.GetManifestResourceNames();
            List<string> resoruceList = resources.ToList();
            foreach (string s in resoruceList)
            {
                using (System.IO.Stream stream = System.Reflection.Assembly.GetExecutingAssembly().GetManifestResourceStream(s))
                {
                    using (System.IO.FileStream fileStream = new System.IO.FileStream(System.IO.Path.Combine(System.IO.Path.GetDirectoryName(filePath), resourceName), System.IO.FileMode.Create))
                    {
                        for (int i = 0; i < stream.Length; i++)
                        {
                            fileStream.WriteByte((byte)stream.ReadByte());
                        }
                        fileStream.Close();
                    }
                }
            }
        }

        #region Assign Parameter Values
        private void SetParameters(FamilyInstance fi, List<RevitParameter> parameters)
        {
            foreach (RevitParameter rp in parameters)
            {
                try
                {
                    Parameter p = fi.get_Parameter(rp.ParameterName);
                    switch (rp.StorageType)
                    {
                        case "Double":
                            p.Set(Convert.ToDouble(rp.Value));
                            break;
                        case "Integer":
                            p.Set(Convert.ToInt32(rp.Value));
                            break;
                        case "String":
                            p.Set(rp.Value);
                            break;
                        case "ElementId":
                            p.Set(new ElementId(Convert.ToInt32(rp.Value)));
                            break;
                        default:
                            p.Set(rp.Value);
                            break;
                    }
                }
                catch (Exception ex)
                {
                    TaskDialog.Show("Error", ex.Message);
                }
            }
        }

        private void SetParameters(Wall wall, List<RevitParameter> parameters)
        {
            foreach (RevitParameter rp in parameters)
            {
                try
                {
                    Parameter p = wall.get_Parameter(rp.ParameterName);
                    switch (rp.StorageType)
                    {
                        case "Double":
                            p.Set(Convert.ToDouble(rp.Value));
                            break;
                        case "Integer":
                            p.Set(Convert.ToInt32(rp.Value));
                            break;
                        case "String":
                            p.Set(rp.Value);
                            break;
                        case "ElementId":
                            p.Set(new ElementId(Convert.ToInt32(rp.Value)));
                            break;
                        default:
                            p.Set(rp.Value);
                            break;
                    }
                }
                catch (Exception ex)
                {
                    TaskDialog.Show("Error", ex.Message);
                }
            }
        }

        private void SetParameters(Floor floor, List<RevitParameter> parameters)
        {
            foreach (RevitParameter rp in parameters)
            {
                try
                {
                    Parameter p = floor.get_Parameter(rp.ParameterName);
                    switch (rp.StorageType)
                    {
                        case "Double":
                            p.Set(Convert.ToDouble(rp.Value));
                            break;
                        case "Integer":
                            p.Set(Convert.ToInt32(rp.Value));
                            break;
                        case "String":
                            p.Set(rp.Value);
                            break;
                        case "ElementId":
                            p.Set(new ElementId(Convert.ToInt32(rp.Value)));
                            break;
                        default:
                            p.Set(rp.Value);
                            break;
                    }
                }
                catch (Exception ex)
                {
                    TaskDialog.Show("Error", ex.Message);
                }
            }
        }

        private void SetParameters(FootPrintRoof roof, List<RevitParameter> parameters)
        {
            foreach (RevitParameter rp in parameters)
            {
                try
                {
                    Parameter p = roof.get_Parameter(rp.ParameterName);
                    switch (rp.StorageType)
                    {
                        case "Double":
                            p.Set(Convert.ToDouble(rp.Value));
                            break;
                        case "Integer":
                            p.Set(Convert.ToInt32(rp.Value));
                            break;
                        case "String":
                            p.Set(rp.Value);
                            break;
                        case "ElementId":
                            p.Set(new ElementId(Convert.ToInt32(rp.Value)));
                            break;
                        default:
                            p.Set(rp.Value);
                            break;
                    }
                }
                catch (Exception ex)
                {
                    TaskDialog.Show("Error", ex.Message);
                }
            }
        }
        
        #endregion

        #region Assign the GUID
        private void AssignGuid(FamilyInstance fi, Guid guid, Schema instanceSchema)
        {
            try
            {
                Entity entity = new Entity(instanceSchema);
                Field field = instanceSchema.GetField("InstanceID");
                entity.Set<string>(field, guid.ToString());
                fi.SetEntity(entity);
            }
            catch (Exception ex)
            {
                TaskDialog.Show("Error", ex.Message);
            }
        }

        private void AssignGuid(Wall wall, Guid guid, Schema instanceSchema)
        {
            try
            {
                Entity entity = new Entity(instanceSchema);
                Field field = instanceSchema.GetField("InstanceID");
                entity.Set<string>(field, guid.ToString());
                wall.SetEntity(entity);
            }
            catch (Exception ex)
            {
                TaskDialog.Show("Error", ex.Message);
            }
        }

        private void AssignGuid(Floor floor, Guid guid, Schema instanceSchema)
        {
            try
            {
                Entity entity = new Entity(instanceSchema);
                Field field = instanceSchema.GetField("InstanceID");
                entity.Set<string>(field, guid.ToString());
                floor.SetEntity(entity);
            }
            catch (Exception ex)
            {
                TaskDialog.Show("Error", ex.Message);
            }
        }

        private void AssignGuid(FootPrintRoof roof, Guid guid, Schema instanceSchema)
        {
            try
            {
                Entity entity = new Entity(instanceSchema);
                Field field = instanceSchema.GetField("InstanceID");
                entity.Set<string>(field, guid.ToString());
                roof.SetEntity(entity);
            }
            catch (Exception ex)
            {
                TaskDialog.Show("Error", ex.Message);
            }
        }
        #endregion
    }
}