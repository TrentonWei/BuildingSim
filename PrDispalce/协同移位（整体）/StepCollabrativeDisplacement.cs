﻿using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Windows.Forms;

using ESRI.ArcGIS.esriSystem;
using ESRI.ArcGIS.Controls;
using ESRI.ArcGIS.Carto;
using ESRI.ArcGIS.Display;
using ESRI.ArcGIS.Geometry;
using ESRI.ArcGIS.GlobeCore;
using ESRI.ArcGIS.Geodatabase;
using ESRI.ArcGIS.DataSourcesGDB;
using ESRI.ArcGIS.DataSourcesFile;
using ESRI.ArcGIS.ADF;
using ESRI.ArcGIS.SystemUI;
using AuxStructureLib;
using PrDispalce.工具类.CollabrativeDisplacement;

namespace PrDispalce.协同移位_整体_
{
    public partial class StepCollabrativeDisplacement : Form
    {
        public StepCollabrativeDisplacement(IMap mMap)
        {
            InitializeComponent();
            this.pMap = mMap;
        }

        #region 参数
        string localFilePath, fileNameExt, FilePath;
        IMap pMap;
        PrDispalce.工具类.FeatureHandle pFeatureHandle = new 工具类.FeatureHandle();
        #endregion

        #region 初始化
        private void StepCollabrativeDisplacement_Load(object sender, EventArgs e)
        {
            if (this.pMap.LayerCount <= 0)
                return;

            ILayer pLayer;
            string strLayerName;
            for (int i = 0; i < this.pMap.LayerCount; i++)
            {
                pLayer = this.pMap.get_Layer(i);
                strLayerName = pLayer.Name;

                IDataset LayerDataset = pLayer as IDataset;

                if (LayerDataset != null)
                {
                    #region 添加线图层
                    IFeatureLayer pFeatureLayer = pLayer as IFeatureLayer;
                    if (pFeatureLayer.FeatureClass.ShapeType == esriGeometryType.esriGeometryPolyline)
                    {
                        this.comboBox1.Items.Add(strLayerName);
                    }
                    #endregion

                    #region 添加面图层
                    if (pFeatureLayer.FeatureClass.ShapeType == esriGeometryType.esriGeometryPolygon)
                    {
                        this.comboBox2.Items.Add(strLayerName);
                    }
                    #endregion
                }
            }

            #region 默认显示第一个
            if (this.comboBox1.Items.Count > 0)
            {
                this.comboBox1.SelectedIndex = 0;
            }

            if (this.comboBox2.Items.Count > 0)
            {
                this.comboBox2.SelectedIndex = 0;
            }
            #endregion
        }
        #endregion

        #region 输出路径
        private void button1_Click(object sender, EventArgs e)
        {
            SaveFileDialog saveFileDialog1 = new SaveFileDialog();
            saveFileDialog1.Filter = " shp files(*.shp)|";

            if (saveFileDialog1.ShowDialog() == DialogResult.OK)
            {
                //获得文件路径
                localFilePath = saveFileDialog1.FileName.ToString();

                //获取文件名，不带路径
                fileNameExt = localFilePath.Substring(localFilePath.LastIndexOf("\\") + 1);

                //获取文件路径，不带文件名
                FilePath = localFilePath.Substring(0, localFilePath.LastIndexOf("\\"));
            }

            this.comboBox3.Text = localFilePath;
        }
        #endregion

        #region 场移位+局部移位，解决原生冲突
        private void button2_Click(object sender, EventArgs e)
        {
            AuxStructureLib.ConflictLib.ConflictDetector cd = new AuxStructureLib.ConflictLib.ConflictDetector();

            #region 获取图层
            List<IFeatureLayer> list = new List<IFeatureLayer>();
            IFeatureLayer StreetLayer = pFeatureHandle.GetLayer(pMap, this.comboBox1.Text.ToString());
            IFeatureLayer BuildingLayer = pFeatureHandle.GetLayer(pMap, this.comboBox2.Text.ToString());
            list.Add(StreetLayer);
            list.Add(BuildingLayer);
            #endregion

            #region 数据读取并内插
            SMap map = new SMap(list);
            map.ReadDateFrmEsriLyrsForEnrichNetWork();
            map.InterpretatePoint(2);

            //备份map
            SMap mapCopy = new SMap(list);
            mapCopy.ReadDateFrmEsriLyrsForEnrichNetWork();
            mapCopy.InterpretatePoint(2);

            SMap mapCopy2 = new SMap(list);
            mapCopy2.ReadDateFrmEsriLyrsForEnrichNetWork();
            mapCopy2.InterpretatePoint(2);
            #endregion

            #region DT+CDT+SKE
            DelaunayTin dt = new DelaunayTin(mapCopy.TriNodeList);
            dt.CreateDelaunayTin(AlgDelaunayType.Side_extent);

            ConvexNull cn = new ConvexNull(dt.TriNodeList);
            cn.CreateConvexNull();

            ConsDelaunayTin cdt = new ConsDelaunayTin(dt);
            cdt.CreateConsDTfromPolylineandPolygon(mapCopy.PolylineList, mapCopy.PolygonList);

            Triangle.WriteID(dt.TriangleList);
            TriEdge.WriteID(dt.TriEdgeList);

            AuxStructureLib.Skeleton ske = new AuxStructureLib.Skeleton(cdt, mapCopy);
            ske.TranverseSkeleton_Segment_NT_DeleteIntraSkel();
            ske.TranverseSkeleton_Segment_PLP_NONull();

            ProxiGraph pg = new ProxiGraph();
            pg.CreateProxiGraphfrmSkeletonBuildings(mapCopy, ske);
            pg.DeleteRepeatedEdge(pg.EdgeList);

            VoronoiDiagram vd = new AuxStructureLib.VoronoiDiagram(ske, pg, mapCopy); //只用于基于邻近图计算场
            pg.CreatePgwithoutlongEdges(pg.NodeList, pg.EdgeList, 0.0018);
            #endregion

            #region 冲突探测；建立移位场并移位
            PrDispalce.工具类.CollabrativeDisplacement.CConflictDetector ccd = new CConflictDetector();//创建冲突探测工具对象
            ccd.ConflictDetectByPg(pg.EdgeList, double.Parse(this.textBox1.Text), double.Parse(this.textBox2.Text), mapCopy.PolygonList, mapCopy.PolylineList);

            //pg.WriteProxiGraph2Shp(FilePath, "冲突边", pMap.SpatialReference, pg.NodeList, ccd.ConflictEdge);
            #region 基于道路场的移位
            vd.FieldBuildBasedonRoads2(pg.PgwithouLongEdgesEdgesList, mapCopy.PolygonList);//建立基于道路的场
            PrDispalce.工具类.CollabrativeDisplacement.ForceComputation pForceComputaiton = new 工具类.CollabrativeDisplacement.ForceComputation();
            pForceComputaiton.FieldBasedForceComputation2(double.Parse(this.textBox3.Text), vd.FielderListforRoads1, pg.PgwithoutLongEdgesNodesList, pg.PgwithouLongEdgesEdgesList, 5);
            pForceComputaiton.UpdataCoordsforPGbyForce_Group2(pg.NodeList, map, pForceComputaiton.CombinationForceListforRoadsField);//更新建筑物位置(对map做更新)
            pForceComputaiton.UpdataCoordsforPGbyForce_Group2(pg.NodeList, mapCopy2, pForceComputaiton.CombinationForceListforRoadsField);//更新建筑物位置(对map做更新)
            #endregion

            #region 基于建筑物间冲突的移位
            for (int i = 0; i < ccd.ConflictEdge.Count; i++)
            {
                ProxiEdge Pe1 = ccd.ConflictEdge[i];
                if (Pe1.Node1.FeatureType == FeatureType.PolygonType && Pe1.Node2.FeatureType == FeatureType.PolygonType)
                {
                    PolygonObject Po1 = null; PolygonObject Po2 = null;

                    #region 找到冲突边对应的建筑物
                    for (int j = 0; j < mapCopy.PolygonList.Count; j++)
                    {
                        if (Pe1.Node1.TagID == mapCopy.PolygonList[j].ID && mapCopy.PolygonList[j].FeatureType == FeatureType.PolygonType)
                        {
                            Po1 = mapCopy.PolygonList[j];
                        }

                        if (Pe1.Node2.TagID == mapCopy.PolygonList[j].ID && mapCopy.PolygonList[j].FeatureType == FeatureType.PolygonType)
                        {
                            Po2 = mapCopy.PolygonList[j];
                        }
                    }
                    #endregion

                    vd.FieldBuildBasedonBuildings2(Po1.ID, Po2.ID, pg.PgwithouLongEdgesEdgesList, mapCopy.PolygonList);
                    pForceComputaiton.FieldBasedForceComputationforBuildings(vd.FielderListforBuildings1, pg.PgwithoutLongEdgesNodesList, pg.PgwithouLongEdgesEdgesList, 3, 3, double.Parse(this.textBox2.Text)*1.1);
                    pForceComputaiton.UpdataCoordsforPGbyForce_Group2(pg.NodeList, map, pForceComputaiton.CombinationForceListforBuildingField);//更新建筑物位置（对map做更新）
                    pForceComputaiton.UpdataCoordsforPGbyForce_Group2(pg.NodeList, mapCopy2, pForceComputaiton.CombinationForceListforBuildingField);//更新建筑物位置（对map做更新）
                }
            }
            #endregion
            #endregion

            //pg.WriteProxiGraph2Shp(FilePath, "邻近图", pMap.SpatialReference, pg.NodeList, pg.EdgeList); 
            //map.WriteResult2Shp(FilePath, pMap.SpatialReference);
            //mapCopy.WriteResult2Shp(FilePath, pMap.SpatialReference);
            #region 第一次移位后次生冲突探测并移位
            #region 对移位后的建筑物群重构邻近关系
            DelaunayTin dt1 = new DelaunayTin(mapCopy2.TriNodeList);
            dt1.CreateDelaunayTin(AlgDelaunayType.Side_extent);

            ConvexNull cn1 = new ConvexNull(dt1.TriNodeList);
            cn1.CreateConvexNull();

            ConsDelaunayTin cdt1 = new ConsDelaunayTin(dt1);
            cdt1.CreateConsDTfromPolylineandPolygon(mapCopy2.PolylineList, mapCopy2.PolygonList);

            Triangle.WriteID(dt1.TriangleList);
            TriEdge.WriteID(dt1.TriEdgeList);

            AuxStructureLib.Skeleton ske1 = new AuxStructureLib.Skeleton(cdt1, mapCopy2);
            ske1.TranverseSkeleton_Segment_NT_DeleteIntraSkel();
            ske1.TranverseSkeleton_Segment_PLP_NONull();

            ProxiGraph pg1 = new ProxiGraph();
            pg1.CreateProxiGraphfrmSkeletonBuildings(mapCopy2, ske1);
            pg1.DeleteRepeatedEdge(pg1.EdgeList);
            pg1.CreatePgwithoutlongEdges(pg1.NodeList, pg1.EdgeList, 0.0018);
            #endregion
            //mapCopy2.WriteResult2Shp(FilePath, pMap.SpatialReference);
            //pg1.WriteProxiGraph2Shp(FilePath, "邻近图", pMap.SpatialReference, pg1.NodeList, pg1.PgwithouLongEdgesEdgesList);
            #endregion

            #region 探测未被解决的原生冲突并局部移位
            PrDispalce.工具类.CollabrativeDisplacement.CConflictDetector ccd1 = new CConflictDetector();//创建冲突探测工具对象
            ccd1.ConflictDetectByPg(pg1.PgwithouLongEdgesEdgesList, double.Parse(this.textBox1.Text), double.Parse(this.textBox2.Text), mapCopy2.PolygonList, mapCopy2.PolylineList);
            //pg.WriteProxiGraph2Shp(FilePath, "冲突边", pMap.SpatialReference, pg.NodeList, ccd1.ConflictEdge);
            for (int i = 0; i < ccd1.ConflictEdge.Count; i++)
            {
                ProxiEdge Pe1 = ccd1.ConflictEdge[i];

                //如果建筑物是原生冲突
                if (ccd1.IsOneInitialConflictSolved(Pe1, ccd.ConflictEdge))
                {
                    #region 建筑物与道路次生冲突
                    if (Pe1.Node1.FeatureType == FeatureType.PolygonType && Pe1.Node2.FeatureType == FeatureType.PolylineType)//node2在道路上
                    {
                        double f = (double.Parse(this.textBox1.Text) - Pe1.NearestEdge.NearestDistance) * 1.1;
                        double cos = (Pe1.NearestEdge.Point1.X - Pe1.NearestEdge.Point2.X) / Pe1.NearestEdge.NearestDistance;
                        double sin = (Pe1.NearestEdge.Point1.Y - Pe1.NearestEdge.Point2.Y) / Pe1.NearestEdge.NearestDistance;

                        #region 找到对应建筑物更新位置
                        for (int j = 0; j < mapCopy2.PolygonList.Count; j++)
                        {
                            if (Pe1.Node1.TagID == mapCopy2.PolygonList[j].ID && mapCopy2.PolygonList[j].FeatureType == FeatureType.PolygonType)
                            {
                                foreach (TriNode curPoint in map.PolygonList[j].PointList)
                                {
                                    curPoint.X += f * cos;
                                    curPoint.Y += f * sin;
                                }
                            }
                        }
                        #endregion
                    }

                    if (Pe1.Node1.FeatureType == FeatureType.PolylineType && Pe1.Node2.FeatureType == FeatureType.PolygonType)//node1在道路上
                    {
                        double f = (double.Parse(this.textBox1.Text) - Pe1.NearestEdge.NearestDistance) * 1.1;
                        double cos = (Pe1.NearestEdge.Point2.X - Pe1.NearestEdge.Point1.X) / Pe1.NearestEdge.NearestDistance;
                        double sin = (Pe1.NearestEdge.Point2.Y - Pe1.NearestEdge.Point1.Y) / Pe1.NearestEdge.NearestDistance;

                        #region 找到对应建筑物更新位置
                        for (int j = 0; j < mapCopy2.PolygonList.Count; j++)
                        {
                            if (Pe1.Node2.TagID == mapCopy2.PolygonList[j].ID && mapCopy2.PolygonList[j].FeatureType == FeatureType.PolygonType)
                            {
                                foreach (TriNode curPoint in map.PolygonList[j].PointList)
                                {
                                    curPoint.X += f * cos;
                                    curPoint.Y += f * sin;
                                }
                            }
                        }
                        #endregion
                    }
                    #endregion

                    #region 建筑物间次生冲突
                    if (Pe1.Node1.FeatureType == FeatureType.PolygonType && Pe1.Node2.FeatureType == FeatureType.PolygonType)
                    {
                        PolygonObject Po1 = null; PolygonObject Po2 = null;

                        #region 找到冲突边对应的建筑物
                        for (int j = 0; j < mapCopy2.PolygonList.Count; j++)
                        {
                            if (Pe1.Node1.TagID == mapCopy2.PolygonList[j].ID && mapCopy2.PolygonList[j].FeatureType == FeatureType.PolygonType)
                            {
                                Po1 = mapCopy2.PolygonList[j];
                            }

                            if (Pe1.Node2.TagID == mapCopy2.PolygonList[j].ID && mapCopy2.PolygonList[j].FeatureType == FeatureType.PolygonType)
                            {
                                Po2 = mapCopy2.PolygonList[j];
                            }
                        }
                        #endregion

                        vd.FieldBuildBasedonBuildings2(Po1.ID, Po2.ID, pg1.PgwithouLongEdgesEdgesList, mapCopy2.PolygonList);
                        pForceComputaiton.FieldBasedForceComputationforBuildings(vd.FielderListforBuildings1, pg1.PgwithoutLongEdgesNodesList, pg1.PgwithouLongEdgesEdgesList, 3, 0, double.Parse(this.textBox2.Text)*1.1);
                        pForceComputaiton.UpdataCoordsforPGbyForce_Group2(pg1.NodeList, map, pForceComputaiton.CombinationForceListforBuildingField);//更新建筑物位置（对map做更新）
                    }
                    #endregion
                }
            }
            #endregion

            #region 冲突探测+移位结果输出

            #region 重新构造局部移位后的邻近关系
            #region 对移位后的建筑物群重构邻近关系
            DelaunayTin dt2 = new DelaunayTin(map.TriNodeList);
            dt2.CreateDelaunayTin(AlgDelaunayType.Side_extent);

            ConvexNull cn2 = new ConvexNull(dt2.TriNodeList);
            cn2.CreateConvexNull();

            ConsDelaunayTin cdt2 = new ConsDelaunayTin(dt2);
            cdt2.CreateConsDTfromPolylineandPolygon(map.PolylineList, map.PolygonList);

            Triangle.WriteID(dt2.TriangleList);
            TriEdge.WriteID(dt2.TriEdgeList);

            AuxStructureLib.Skeleton ske2 = new AuxStructureLib.Skeleton(cdt2, map);
            ske2.TranverseSkeleton_Segment_NT_DeleteIntraSkel();
            ske2.TranverseSkeleton_Segment_PLP_NONull();

            ProxiGraph pg2 = new ProxiGraph();
            pg2.CreateProxiGraphfrmSkeletonBuildings(map, ske2);
            pg2.DeleteRepeatedEdge(pg2.EdgeList);
            pg2.CreatePgwithoutlongEdges(pg2.NodeList, pg2.EdgeList, 0.0018);
            #endregion
            #endregion

            PrDispalce.工具类.CollabrativeDisplacement.CConflictDetector ccd2 = new CConflictDetector();//创建冲突探测工具对象
            ccd2.ConflictDetectByPg(pg2.PgwithouLongEdgesEdgesList, double.Parse(this.textBox1.Text), double.Parse(this.textBox2.Text), map.PolygonList, map.PolylineList);

            //pg.WriteProxiGraph2Shp(FilePath, "冲突边", pMap.SpatialReference, pg.NodeList, ccd2.ConflictEdge);
            #region 若原生冲突未被解决，返回需要被解决的边
            if (ccd2.IsInitialConflictSolved(ccd.ConflictEdge, ccd2.ConflictEdge))
            {
                //找到未被解决的原生冲突
                List<ProxiEdge> InvolvedEdges = ccd2.ReturnInvolvedInitialEdges(ccd.ConflictEdge, ccd2.ConflictEdge);
                //返回需要处理的原生冲突
                ProxiEdge pTargetEdge = ccd2.ReturnEdgeTobeSolved(InvolvedEdges);

                MessageBox.Show(pTargetEdge.Node1.TagID.ToString() + pTargetEdge.Node1.FeatureType.ToString() + ";" + pTargetEdge.Node2.TagID.ToString() + pTargetEdge.Node2.FeatureType.ToString());
            }
            #endregion
            map.WriteResult2Shp(FilePath, pMap.SpatialReference);
            #endregion
        }
        #endregion

        #region 局部移位，解决新产生的冲突
        private void button3_Click(object sender, EventArgs e)
        {
            #region 获取图层
            List<IFeatureLayer> list = new List<IFeatureLayer>();
            IFeatureLayer StreetLayer = pFeatureHandle.GetLayer(pMap, this.comboBox1.Text.ToString());
            IFeatureLayer BuildingLayer = pFeatureHandle.GetLayer(pMap, this.comboBox2.Text.ToString());
            list.Add(StreetLayer);
            list.Add(BuildingLayer);
            #endregion

            #region 数据读取并内插
            SMap map = new SMap(list);
            map.ReadDateFrmEsriLyrsForEnrichNetWork();
            map.InterpretatePoint(2);

            //备份map
            SMap mapCopy = new SMap(list);
            mapCopy.ReadDateFrmEsriLyrsForEnrichNetWork();
            mapCopy.InterpretatePoint(2);
            #endregion

            #region DT+CDT+SKE
            DelaunayTin dt = new DelaunayTin(mapCopy.TriNodeList);
            dt.CreateDelaunayTin(AlgDelaunayType.Side_extent);

            ConvexNull cn = new ConvexNull(dt.TriNodeList);
            cn.CreateConvexNull();

            ConsDelaunayTin cdt = new ConsDelaunayTin(dt);
            cdt.CreateConsDTfromPolylineandPolygon(mapCopy.PolylineList, mapCopy.PolygonList);

            Triangle.WriteID(dt.TriangleList);
            TriEdge.WriteID(dt.TriEdgeList);

            AuxStructureLib.Skeleton ske = new AuxStructureLib.Skeleton(cdt, mapCopy);
            ske.TranverseSkeleton_Segment_NT_DeleteIntraSkel();
            ske.TranverseSkeleton_Segment_PLP_NONull();

            ProxiGraph pg = new ProxiGraph();
            pg.CreateProxiGraphfrmSkeletonBuildings(mapCopy, ske);
            pg.DeleteRepeatedEdge(pg.EdgeList);

            VoronoiDiagram vd = new AuxStructureLib.VoronoiDiagram(ske, pg, mapCopy); ;//只用于基于邻近图计算场
            pg.CreatePgwithoutlongEdges(pg.NodeList, pg.EdgeList, 0.0018);
            #endregion

            #region 冲突探测；局部移位
            PrDispalce.工具类.CollabrativeDisplacement.CConflictDetector ccd = new CConflictDetector();//创建冲突探测工具对象
            ccd.ConflictDetectByPg(pg.EdgeList, double.Parse(this.textBox1.Text), double.Parse(this.textBox2.Text), mapCopy.PolygonList, mapCopy.PolylineList);

            PrDispalce.工具类.CollabrativeDisplacement.ForceComputation pForceComputaiton = new 工具类.CollabrativeDisplacement.ForceComputation();
       
            #region 次生冲突的移位
            for (int i = 0; i < ccd.ConflictEdge.Count; i++)
            {
                ProxiEdge Pe1 = ccd.ConflictEdge[i];

                #region 建筑物与道路次生冲突
                if (Pe1.Node1.FeatureType == FeatureType.PolygonType && Pe1.Node2.FeatureType == FeatureType.PolylineType)//node2在道路上
                {
                    double f = (double.Parse(this.textBox1.Text) - Pe1.NearestEdge.NearestDistance) * 1.05;
                    double cos = (Pe1.NearestEdge.Point1.X - Pe1.NearestEdge.Point2.X) / Pe1.NearestEdge.NearestDistance;
                    double sin = (Pe1.NearestEdge.Point1.Y - Pe1.NearestEdge.Point2.Y) / Pe1.NearestEdge.NearestDistance;

                    #region 找到对应建筑物更新位置
                    for (int j = 0; j < mapCopy.PolygonList.Count; j++)
                    {
                        if (Pe1.Node1.TagID == mapCopy.PolygonList[j].ID && mapCopy.PolygonList[j].FeatureType == FeatureType.PolygonType)
                        {
                            foreach (TriNode curPoint in map.PolygonList[j].PointList)
                            {
                                curPoint.X += f * cos;
                                curPoint.Y += f * sin;
                            }
                        }
                    }
                    #endregion
                }

                if (Pe1.Node1.FeatureType == FeatureType.PolylineType && Pe1.Node2.FeatureType == FeatureType.PolygonType)//node1在道路上
                {
                    double f = (double.Parse(this.textBox1.Text) - Pe1.NearestEdge.NearestDistance) * 1.05;
                    double cos = (Pe1.NearestEdge.Point2.X - Pe1.NearestEdge.Point1.X) / Pe1.NearestEdge.NearestDistance;
                    double sin = (Pe1.NearestEdge.Point2.Y - Pe1.NearestEdge.Point1.Y) / Pe1.NearestEdge.NearestDistance;

                    #region 找到对应建筑物更新位置
                    for (int j = 0; j < mapCopy.PolygonList.Count; j++)
                    {
                        if (Pe1.Node2.TagID == mapCopy.PolygonList[j].ID && mapCopy.PolygonList[j].FeatureType == FeatureType.PolygonType)
                        {
                            foreach (TriNode curPoint in map.PolygonList[j].PointList)
                            {
                                curPoint.X += f * cos;
                                curPoint.Y += f * sin;
                            }
                        }
                    }
                    #endregion
                }
                #endregion

                #region 如果是建筑物间冲突
                if (Pe1.Node1.FeatureType == FeatureType.PolygonType && Pe1.Node2.FeatureType == FeatureType.PolygonType)
                {
                    PolygonObject Po1 = null; PolygonObject Po2 = null;

                    #region 找到冲突边对应的建筑物
                    for (int j = 0; j < mapCopy.PolygonList.Count; j++)
                    {
                        if (Pe1.Node1.TagID == mapCopy.PolygonList[j].ID && mapCopy.PolygonList[j].FeatureType == FeatureType.PolygonType)
                        {
                            Po1 = mapCopy.PolygonList[j];
                        }

                        if (Pe1.Node2.TagID == mapCopy.PolygonList[j].ID && mapCopy.PolygonList[j].FeatureType == FeatureType.PolygonType)
                        {
                            Po2 = mapCopy.PolygonList[j];
                        }
                    }
                    #endregion

                    vd.FieldBuildBasedonBuildings2(Po1.ID, Po2.ID, pg.PgwithouLongEdgesEdgesList, mapCopy.PolygonList);
                    pForceComputaiton.FieldBasedForceComputationforBuildings(vd.FielderListforBuildings1, pg.PgwithoutLongEdgesNodesList, pg.PgwithouLongEdgesEdgesList, 3, 0, double.Parse(this.textBox2.Text) * 1.1);
                    pForceComputaiton.UpdataCoordsforPGbyForce_Group2(pg.NodeList, map, pForceComputaiton.CombinationForceListforBuildingField);//更新建筑物位置（对map做更新）                   
                }
                #endregion

            }
            #endregion
            #endregion

            #region 重构移位后的邻近关系，并冲突探测
            DelaunayTin dt1 = new DelaunayTin(map.TriNodeList);
            dt1.CreateDelaunayTin(AlgDelaunayType.Side_extent);

            ConvexNull cn1 = new ConvexNull(dt1.TriNodeList);
            cn1.CreateConvexNull();

            ConsDelaunayTin cdt1 = new ConsDelaunayTin(dt1);
            cdt1.CreateConsDTfromPolylineandPolygon(map.PolylineList, map.PolygonList);

            Triangle.WriteID(dt1.TriangleList);
            TriEdge.WriteID(dt1.TriEdgeList);

            AuxStructureLib.Skeleton ske1 = new AuxStructureLib.Skeleton(cdt1, map);
            ske1.TranverseSkeleton_Segment_NT_DeleteIntraSkel();
            ske1.TranverseSkeleton_Segment_PLP_NONull();

            ProxiGraph pg1 = new ProxiGraph();
            pg1.CreateProxiGraphfrmSkeletonBuildings(map, ske1);
            pg1.DeleteRepeatedEdge(pg1.EdgeList);
            pg1.CreatePgwithoutlongEdges(pg1.NodeList, pg1.EdgeList, 0.0018);

            PrDispalce.工具类.CollabrativeDisplacement.CConflictDetector ccd1 = new CConflictDetector();//创建冲突探测工具对象
            ccd1.ConflictDetectByPg(pg1.EdgeList, double.Parse(this.textBox1.Text), double.Parse(this.textBox2.Text), map.PolygonList, map.PolylineList);
            #endregion

            pg.WriteProxiGraph2Shp(FilePath, "冲突边", pMap.SpatialReference, pg1.NodeList, ccd1.ConflictEdge);
            #region
            if (ccd1.ConflictEdge.Count > 0)
            {
                //找到相关的次生冲突
                List<ProxiEdge> InvolvedEdges = ccd1.ReturnInvolvedEdges(ccd.ConflictEdge, ccd1.ConflictEdge);
                //找到待解决的冲突
                ProxiEdge pTargetEdge = ccd1.ReturnEdgeTobeSolved(InvolvedEdges);

                MessageBox.Show(pTargetEdge.Node1.TagID.ToString() + pTargetEdge.Node1.FeatureType.ToString() + ";" + pTargetEdge.Node2.TagID.ToString() + pTargetEdge.Node2.FeatureType.ToString());
            }
            #endregion

            map.WriteResult2Shp(FilePath, pMap.SpatialReference);
        }
        #endregion
    }
}
