﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using MessagePack;
using SliccDB.Core;
using SliccDB.Exceptions;
using SliccDB.Fluent;

namespace SliccDB.Serialization
{
    public class DatabaseConnection
    {
        public string FilePath { get; private set; }

        internal Database Database { get; private set; }

        public HashSet<Node> Nodes => Database?.Nodes;
        public HashSet<Relation> Relations => Database?.Relations;

        public ConnectionStatus ConnectionStatus { get; set; }
        private readonly bool realtime;

        /// <summary>
        /// Creates new Database Connection Instance
        /// </summary>
        /// <param name="filePath">Path to a database file</param>
        /// <param name="realtime">if true, attempts to save the database on every operation. Disabled by default as it is memory intensive</param>
        public DatabaseConnection(string filePath, bool realtime = false)
        {
            FilePath = filePath;
            if (File.Exists(filePath))
            {
                var bytes = File.ReadAllBytes(filePath);
                try
                {
                    Database = MessagePackSerializer.Deserialize<Database>(bytes);
                    this.ConnectionStatus = ConnectionStatus.Connected;
                }
                catch (Exception exception)
                {
                    Database = new Database();
                    this.ConnectionStatus = ConnectionStatus.Connected;

                }
            }
            else
            {
                Database = new Database();
                this.ConnectionStatus = ConnectionStatus.Connected;
            }
            this.realtime = realtime;
        }

        public IEnumerable<Node> QueryNodes(Func<HashSet<Node>, IEnumerable<Node>> query)
        {
            return query.Invoke(Nodes);
        }

        public IEnumerable<Relation> QueryRelations(Func<HashSet<Relation>, IEnumerable<Relation>> query)
        {
            return query.Invoke(Relations);
        }

        public void Update(GraphEntity entity)
        {
            if (entity is Node node)
            {
                Nodes.Replace(node);
            }
            else if (entity is Relation relation)
            {
                Relations.Replace(relation);
            }
            else
            {
                throw new InvalidOperationException("Invalid entity type");
            }
            SaveDatabase();
        }

        public int Delete(GraphEntity entity)
        {
            var nodes = Nodes.RemoveWhere(n => n.Hash == entity.Hash);
            var relationsLinkedWithNodes = Relations.RemoveWhere(r => r.SourceHash == entity.Hash || r.TargetHash == entity.Hash);
            var relations = Relations.RemoveWhere(r => r.Hash == entity.Hash);

            SaveDatabase();
            return nodes + relationsLinkedWithNodes + relations;
        }

        public IEnumerable<Node> FindNodesWithRelationSource(string relationName)
        {
            var relationsSources = Relations.AsParallel().Where(x => x.RelationName == relationName).Select(x => x.SourceHash);
            return Nodes.AsParallel().Where(x => relationsSources.Contains(x.Hash));
        }

        public IEnumerable<Node> FindNodesWithRelationTarget(string relationName)
        {
            var relationsSources = Relations.AsParallel().Where(x => x.RelationName == relationName).Select(x => x.TargetHash);
            return Nodes.AsParallel().Where(x => relationsSources.Contains(x.Hash));
        }

        public void SaveDatabase()
        {
            if (File.Exists(FilePath))
            {
                var bytes = MessagePackSerializer.Serialize(Database);
                File.WriteAllBytes(FilePath, bytes);
            }
        }

        public void CloseDatabase()
        {
            if (Database != null && ConnectionStatus == ConnectionStatus.Connected)
            {
                this.ConnectionStatus = ConnectionStatus.NotConnected;
                Database.Dispose();
            }
        }

        public void ClearDatabase()
        {
            Nodes.Clear();
            Relations.Clear();
            SaveDatabase();
        }

        public Node CreateNode(Dictionary<string, string> properties = null, HashSet<string> labels = null)
        {
            Dictionary<string, string> props = new Dictionary<string, string>();
            HashSet<string> lablSet = new HashSet<string>();
            var node = new Node(properties ?? props, labels ?? lablSet);
            Database.Nodes.Add(node);
            if(realtime) SaveDatabase();
            return node;
        }

        public void CreateRelation(string relationName, Func<HashSet<Node>, Node> sourceNode, Func<HashSet<Node>, Node> targetNode, Dictionary<string, string> properties = null, HashSet<string> labels = null)
        {
            var sourceNodeObject = sourceNode.Invoke(Database.Nodes);
            var targetNodeObject = targetNode.Invoke(Database.Nodes);
            if (sourceNodeObject is null || targetNodeObject is null)
            {

            }
            bool exists = Relations.AsParallel().ToList().Exists(x =>
                x.TargetHash == targetNodeObject.Hash && x.SourceHash == sourceNodeObject.Hash);
            if (exists)
                throw new RelationExistsException();
            Dictionary<string, string> props = new Dictionary<string, string>();
            HashSet<string> lablSet = new HashSet<string>();
            Database.Relations.Add(new Relation(relationName, properties ?? props, labels ?? lablSet, sourceNodeObject.Hash, targetNodeObject.Hash));
            if (realtime) SaveDatabase();

        }
    }
}