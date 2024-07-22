using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TrafficControlCore.Utils
{
    public class MinHeap
    {
        private List<(int id, int fScore)> heap;
        public MinHeap()
        {
            heap = new List<(int id, int fScore)>();
        }
        //嵌入
        public void Insert(int id, int fScore)
        {
            heap.Add((id, fScore));
            HeapifyUp(heap.Count - 1);
        }
        //提取最小id
        public (int id, int fScore) ExtractMin()
        {
            if (heap.Count == 0) throw new InvalidOperationException("Heap is empty.");

            (int id, int fScore) min = heap[0];
            heap[0] = heap[heap.Count - 1];
            heap.RemoveAt(heap.Count - 1);
            HeapifyDown(0);
            return min;
        }
        private void HeapifyUp(int index)
        {
            while (index > 0)
            {
                int parentIndex = (index - 1) / 2;
                if (heap[parentIndex].fScore <= heap[index].fScore)
                    break;

                (heap[parentIndex], heap[index]) = (heap[index], heap[parentIndex]);
                index = parentIndex;
            }
        }
        private void HeapifyDown(int index)
        {
            int size = heap.Count;
            int leftChildIndex = 2 * index + 1;
            int rightChildIndex = 2 * index + 2;
            int smallestIndex = index;

            if (leftChildIndex < size && heap[leftChildIndex].fScore < heap[smallestIndex].fScore)
                smallestIndex = leftChildIndex;

            if (rightChildIndex < size && heap[rightChildIndex].fScore < heap[smallestIndex].fScore)
                smallestIndex = rightChildIndex;

            if (smallestIndex != index)
            {
                (heap[index], heap[smallestIndex]) = (heap[smallestIndex], heap[index]);
                HeapifyDown(smallestIndex);
            }
        }
    }
}
