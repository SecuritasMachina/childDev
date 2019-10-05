package com.ackdev.utility;

import java.util.logging.Level;
import java.util.logging.Logger;

public class Encrypt {
    public static void main(final String args[]) {
        // System.out.println("Start Test");
        final StringBuffer testData = new StringBuffer("this is a test");// FileUtil.readFile("c:\\temp\\japanesesanscript.txt",
        // "UTF-8"));
        System.currentTimeMillis();
        final Encrypt tmpTest2 = new Encrypt("aeiou", "tusehoami_");
        final String tmpenc = tmpTest2.Encrypt(testData.toString());
        tmpTest2.Encrypt(tmpenc);
        System.currentTimeMillis();
        // System.out.println("Total:" + (EndTime - StartTime));
        try {
            Thread.sleep(100L);
        } catch (final Exception exception) {
        }
        /* return; */
    }

    private static final Logger LOG = Logger.getLogger(Encrypt.class.getName());

    transient private char useKey1[];

    transient private char useKey2[];

    private transient int stringToEncryptLength;

    transient private String mMatrix;

    private transient int mLAM;

    private String mCodeWord;

    private final StringBuffer strCryptMatrix[];

    private char mCodeWordArray[];
    private int codeWordLength;

    public Encrypt() {
        this.stringToEncryptLength = 0;
        this.mMatrix = "8x3p5BeabcdfghijklmnoqrstuvwyzACDEFGHIJKLMNOPQRSTUVWXYZ 1246790-.#/\\!@$<>&*()[]{}';:,?=+~`^|%_";
        this.mLAM = 0;
        this.mCodeWord = "spellcheckerv1";
        this.strCryptMatrix = new StringBuffer[this.mMatrix.length()];
        this.codeWordLength = 0;
        this.initmatrix();
    }

    public Encrypt(final String usekey) {
        this.stringToEncryptLength = 0;
        this.mMatrix = "8x3p5BeabcdfghijklmnoqrstuvwyzACDEFGHIJKLMNOPQRSTUVWXYZ 1246790-.#/\\!@$<>&*()[]{}';:,?=+~`^|%_";
        this.mLAM = 0;
        this.mCodeWord = "spellcheckerv1";
        this.strCryptMatrix = new StringBuffer[this.mMatrix.length()];
        this.codeWordLength = 0;
        this.setUseKey(usekey);
        this.initmatrix();
    }

    public Encrypt(final String usekey, final String Matrix) {
        this.stringToEncryptLength = 0;
        this.mMatrix = "8x3p5BeabcdfghijklmnoqrstuvwyzACDEFGHIJKLMNOPQRSTUVWXYZ 1246790-.#/\\!@$<>&*()[]{}';:,?=+~`^|%_";
        this.mLAM = 0;
        this.mCodeWord = "spellcheckerv1";
        this.strCryptMatrix = new StringBuffer[this.mMatrix.length()];
        this.codeWordLength = 0;
        this.setUseKey(usekey);
        this.mMatrix = Matrix;
        this.initmatrix();
    }

    public String Encrypt(final String psWorkText) {
        int currentPos = 0;
        int Y = 0;
        int Z = 0;
        int MatrixPosition = 0;
        char C2E = ' ';
        char CWL = ' ';
        final char str2EncryptArray[] = psWorkText.toCharArray();
        this.stringToEncryptLength = psWorkText.length();
        final char encryptedArray[] = new char[this.stringToEncryptLength];
        Y = 1;
        char tmpChar = ' ';
        try {
            for (currentPos = 0; currentPos < this.stringToEncryptLength; currentPos++) {
                C2E = str2EncryptArray[currentPos];
                MatrixPosition = this.mMatrix.indexOf(C2E);
                if ((MatrixPosition == -1) | (C2E == '_')) {
                    encryptedArray[currentPos] = C2E;
                } else {
                    CWL = this.mCodeWordArray[Y];
                    for (Z = 1; Z < this.mLAM; Z++) {
                        tmpChar = this.strCryptMatrix[Z].charAt(MatrixPosition);
                        if (tmpChar != CWL) {
                            continue;
                        }
                        encryptedArray[currentPos] = this.strCryptMatrix[Z]
                                .charAt(0);
                        break;
                    }

                    if (++Y == this.codeWordLength) {
                        Y = 1;
                    }
                }
            }

        } catch (final Exception e) {
            Encrypt.LOG.log(Level.SEVERE, e.getMessage(), e);
            // e.printStackTrace();
            Encrypt.LOG.severe("currentPos=" + currentPos);
            Encrypt.LOG.severe("C2E=" + C2E);
            Encrypt.LOG.severe("C2E=" + C2E);
            Encrypt.LOG.severe("MatrixPosition=" + MatrixPosition);
            Encrypt.LOG.severe("Z=" + Z);
        }
        return new String(encryptedArray);
    }

    private void initmatrix() {
        this.mLAM = this.mMatrix.length();
        this.mCodeWordArray = this.mCodeWord.toCharArray();
        this.codeWordLength = this.mCodeWordArray.length;
        synchronized (this.strCryptMatrix) {
            this.strCryptMatrix[1] = new StringBuffer(this.mMatrix);
            for (int X = 2; X < this.mLAM; X++) {
                final String mov1 = this.strCryptMatrix[X - 1].toString()
                        .substring(0, 1);
                final String mov2 = this.strCryptMatrix[X - 1].toString()
                        .substring(1, this.mLAM - 1);
                this.strCryptMatrix[X] = new StringBuffer(mov2 + mov1);
            }

        }
    }

    public String lightEncrypt(final String psWorkText) {
        this.useKey1 = this.mMatrix.toCharArray();
        this.useKey2 = new char[this.useKey1.length + 1];
        for (int x = this.useKey1.length - 1; x > -1; x--) {
            this.useKey2[this.useKey1.length - x] = this.useKey1[x];
        }

        final StringBuffer tmpString = new StringBuffer();
        final int ilen = psWorkText.length();
        synchronized (tmpString) {
            for (int x = 0; x < ilen; x++) {
                final char tmpChar = psWorkText.charAt(x);
                int charVal = -1;
                for (int x2 = this.useKey1.length - 1; x2 > -1; x2--) {
                    if (this.useKey1[x2] != tmpChar) {
                        continue;
                    }
                    charVal = x2;
                    break;
                }

                if (charVal > -1) {
                    tmpString.append(this.useKey2[charVal + 1]);
                } else {
                    tmpString.append(String.valueOf(tmpChar));
                }
            }

        }
        return tmpString.toString();
    }

    public void setUseKey(final String sUseKey) {
        this.mCodeWord = sUseKey;
    }
}
