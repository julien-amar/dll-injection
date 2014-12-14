// TargetUnManagedProcess.cpp : Defines the entry point for the console application.
//

#include "stdafx.h"

#include <iostream>
#include <windows.h>

using std::cout;
using std::cin;
using std::endl;

int _tmain(int argc, _TCHAR* argv[])
{
	HANDLE consoleHandle = GetStdHandle(STD_OUTPUT_HANDLE);

	cout << "Process ID : " << GetCurrentProcessId() << endl;

	Sleep(1000);

	for (int i = 0; true; ++i)
	{
		cout << "[TARGET] Hook triggering." << endl;

		SetConsoleTextAttribute(consoleHandle, i % 2 == 0 ? 10 : 11);

		cout << "[TARGET] Hook triggered." << endl;

		Sleep(1000);
	}

	return 0;
}

