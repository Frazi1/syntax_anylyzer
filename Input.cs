﻿static extern class Library
{
    [JavaRef("Library/printString")]
    static extern void PrintString(string s);
    
    [JavaRef("Library/printInt")]
    static extern void PrintInt(int i);

    [JavaRef("Library/printBool")]
    static extern void PrintBool(bool b);

    [JavaRef("Library/printChar")]
    static extern void PrintChar(char c);
}

class Main
{
    static void Main(string[] args)
    {
        //int a = 13;
        //Library.PrintString("1");
        //Library.PrintInt(2*2+a);
        //Library.PrintChar(a);
        Library.PrintInt(IntTest() + IntTest());
    }

    static int IntTest()
    {
        return 1;
    }
    
    static bool TestChar()
    {
        bool t1 = 10 != 15;
        bool t2 = 1 == 2;
        bool t3 = 1 <= 2;
        bool t4 = 1 >= 2;
        bool t5 = 1 > 2;
        
        return 1 < 2;
    }
}

//static class Test
//{
    
//}

//static class ValeraTest
//{
    //static char Tom = 't';
    //static int Cat = 8 + Tom;

    
    //static int GoodCat(int alla)
    //{
    //    alla = Tom;
    //    return Tom;
    //}

    ////return cast check & cast check
    //static char BadCat()
    //{
    //    return -7;
    //}

    ////array check
    //static void Method(int funArr, char test2)
    //{
    //    int[] arr = new int[4];
    //    arr[(6 - 5)] = Cat;
    //    arr[Cat] = 'r';

    //    //char, not int
    //    //arr['8'] = Cat;
    //}

    //static int Leaf(int[] p)
    //{
    //    ////call not existing func
    //    //lalala(p);

    //    //leaf returns void
    //    int test = Leaf(new int[3]);
    //    //char =  Console.ReadChar();

    //    ////call func with wrong arguments
    //    //Leaf('u');
    //    return 1;
    //}

    //static int ReadTest( int kkk, char ppp)
    //{
    //    int k;
    //    int oo;

    //    oo = kkk + ReadTest(k, ppp);
        
    //    return Console.ReadChar();
    //}
//}
